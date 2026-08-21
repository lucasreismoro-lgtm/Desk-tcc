using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Google.Cloud.Firestore;

namespace SegurançaRe
{
    // === CLASSES DE MODELO (MODELS / ESTRUTURA DE DADOS) ===

    public class DonoModel
    {
        public string Nome { get; set; } = string.Empty;
        public string Cpf { get; set; } = string.Empty;
        public string IdResidencia { get; set; } = string.Empty;
        public string Cep { get; set; } = string.Empty;
        public string Numres { get; set; } = string.Empty;
        public List<MoradorModel> Moradores { get; set; } = new List<MoradorModel>();

        public string DetalhesResidencia => $"Residência: {IdResidencia} | Nº: {Numres} | CEP: {Cep}";
        public string CpfFormatado => string.IsNullOrWhiteSpace(Cpf) ? "" : $"CPF: {Cpf}";
        public string TotalMoradoresTexto => $"Moradores cadastrados: {Moradores.Count}";
    }

    public class MoradorModel
    {
        public string Nome { get; set; } = string.Empty;
        public string Cpf { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Cargo { get; set; } = "morador";
    }

    public class LogModel
    {
        public string Horario { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string Acao { get; set; } = string.Empty;
    }

    public class YoloSimulator
    {
        public async Task IniciarSimulacaoAsync()
        {
            await Task.Delay(100);
        }
    }

    // === CÓDIGO LÓGICO DA TELA (CODE-BEHIND / CONTROLADOR) ===

    public partial class telausuario : ContentPage
    {
        private FirestoreDb? _db;
        private readonly YoloSimulator _simuladorYolo = new YoloSimulator();

        public telausuario()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await InicializarFirebaseEALeitura();
        }

        private async Task InicializarFirebaseEALeitura()
        {
            if (_db == null)
            {
                try
                {
                    string jsonConteudo = string.Empty;

                    try
                    {
                        using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");
                        using var reader = new StreamReader(stream);
                        jsonConteudo = await reader.ReadToEndAsync();
                    }
                    catch (FileNotFoundException)
                    {
                        string pastaProjeto = AppDomain.CurrentDomain.BaseDirectory;
                        string caminhoAlternativo = Path.Combine(pastaProjeto, "..", "..", "..", "..", "..", "Resources", "Raw", "conexao.json");

                        if (File.Exists(caminhoAlternativo))
                        {
                            jsonConteudo = await File.ReadAllTextAsync(caminhoAlternativo);
                        }
                    }

                    if (!string.IsNullOrEmpty(jsonConteudo))
                    {
                        FirestoreDbBuilder builder = new FirestoreDbBuilder
                        {
                            ProjectId = "banco-tcc-dc633",
                            JsonCredentials = jsonConteudo
                        };
                        _db = builder.Build();
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Erro de Conexão", "Não foi possível carregar as credenciais: " + ex.Message, "OK");
                    return;
                }
            }

            await CarregarDadosDashboard();
        }

        private async Task CarregarDadosDashboard()
        {
            if (_db == null) return;

            try
            {
                Query usuariosQuery = _db.Collection("Usuarios").WhereEqualTo("cargo", "dono_da_casa");
                QuerySnapshot usuariosSnapshot = await usuariosQuery.GetSnapshotAsync();

                List<DonoModel> listaDonos = new List<DonoModel>();

                foreach (DocumentSnapshot document in usuariosSnapshot.Documents)
                {
                    if (document.Exists)
                    {
                        document.TryGetValue("nome", out string? nomeDono);
                        document.TryGetValue("cpf", out string? cpfDono);
                        document.TryGetValue("id_residencia", out string? idRes);
                        document.TryGetValue("cep", out string? cepDono);
                        document.TryGetValue("numres", out string? numresDono);

                        var novoDono = new DonoModel
                        {
                            Nome = nomeDono ?? "Sem Nome",
                            Cpf = cpfDono ?? document.Id,
                            IdResidencia = idRes ?? "N/A",
                            Cep = cepDono ?? "N/A",
                            Numres = numresDono ?? "N/A"
                        };

                        try
                        {
                            QuerySnapshot moradoresSnapshot = await document.Reference.Collection("Moradores").GetSnapshotAsync();

                            foreach (DocumentSnapshot moradorDoc in moradoresSnapshot.Documents)
                            {
                                if (moradorDoc.Exists)
                                {
                                    moradorDoc.TryGetValue("nome", out string? nomeMorador);
                                    moradorDoc.TryGetValue("cpf", out string? cpfMorador);
                                    moradorDoc.TryGetValue("email", out string? emailMorador);

                                    novoDono.Moradores.Add(new MoradorModel
                                    {
                                        Nome = nomeMorador ?? "Morador sem Nome",
                                        Cpf = cpfMorador ?? moradorDoc.Id,
                                        Email = emailMorador ?? "Sem E-mail"
                                    });
                                }
                            }
                        }
                        catch { /* Subcoleção vazia ou inexistente */ }

                        listaDonos.Add(novoDono);
                    }
                }

                ListViewUsuarios.ItemsSource = null;
                ListViewUsuarios.ItemsSource = listaDonos;
                LblTotalUsuarios.Text = listaDonos.Count.ToString("D2");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro de Sincronização", "Erro ao processar estrutura do Firestore: " + ex.Message, "OK");
            }
        }

        private async void BtnAtualizarDados_Clicked(object sender, EventArgs e)
        {
            await CarregarDadosDashboard();
        }

        private async void BtnNovoDono_Clicked(object sender, EventArgs e)
        {
            if (_db == null) return;

            string nome = TxtNovoNome.Text?.Trim() ?? string.Empty;
            string cpfRaw = TxtNovoCpf.Text?.Trim() ?? string.Empty;
            string cepRaw = TxtNovoCep.Text?.Trim() ?? string.Empty;
            string codigoCasa = TxtNovoCodigoCasa.Text?.Trim() ?? string.Empty;
            string numeroCasa = TxtNovoNumero.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(cpfRaw) ||
                string.IsNullOrEmpty(cepRaw) || string.IsNullOrEmpty(codigoCasa) ||
                string.IsNullOrEmpty(numeroCasa))
            {
                await DisplayAlert("Aviso", "Preencha todos os campos do formulário.", "OK");
                return;
            }

            string cpfLimpo = Regex.Replace(cpfRaw, @"[^\d]", "");
            string cepLimpo = Regex.Replace(cepRaw, @"[^\d]", "");

            if (cpfLimpo.Length != 11)
            {
                await DisplayAlert("CPF Inválido", "O CPF deve conter 11 dígitos.", "OK");
                return;
            }

            try
            {
                Query queryCodigo = _db.Collection("Usuarios").WhereEqualTo("id_residencia", codigoCasa);
                QuerySnapshot checkCodigo = await queryCodigo.GetSnapshotAsync();

                if (checkCodigo.Documents.Count > 0)
                {
                    await DisplayAlert("Código Indisponível", $"O código '{codigoCasa}' já está sendo utilizado por outra residência.", "OK");
                    return;
                }

                DocumentReference docRef = _db.Collection("Usuarios").Document(cpfLimpo);

                Dictionary<string, object> novoUsuario = new Dictionary<string, object>
                {
                    { "nome", nome },
                    { "cpf", cpfLimpo },
                    { "cargo", "dono_da_casa" },
                    { "id_residencia", codigoCasa },
                    { "cep", cepLimpo },
                    { "numres", numeroCasa }
                };

                await docRef.SetAsync(novoUsuario);

                // Subcoleção: Sensores
                DocumentReference sensoresRef = docRef.Collection("Sensores").Document("estado");
                Dictionary<string, object> estadoInicialSensores = new Dictionary<string, object>
                {
                    { "presencaAtivo", false },
                    { "calorAtivo", false },
                    { "alarmeAtivo", false },
                    { "yoloPessoa", false }
                };
                await sensoresRef.SetAsync(estadoInicialSensores);

                // Subcoleção: Eventos
                DocumentReference eventoRef = docRef.Collection("Eventos").Document();
                Dictionary<string, object> eventoInicial = new Dictionary<string, object>
                {
                    { "sensor", "Sistema" },
                    { "local", "Central de Monitoramento" },
                    { "mensagem", "Sistema de alarme e detecção inicializado com sucesso." },
                    { "dataHora", FieldValue.ServerTimestamp },
                    { "disparado", false }
                };
                await eventoRef.SetAsync(eventoInicial);

                // Subcoleção: Histórico
                DocumentReference historicoRef = docRef.Collection("Historico").Document();
                Dictionary<string, object> eventoInicialHistorico = new Dictionary<string, object>
                {
                    { "tipo", "SISTEMA" },
                    { "mensagem", "Sistema de segurança e registro de histórico inicializados." },
                    { "dataHora", FieldValue.ServerTimestamp }
                };
                await historicoRef.SetAsync(eventoInicialHistorico);

                await DisplayAlert("Sucesso", $"Dono {nome} cadastrado com sucesso!", "OK");

                TxtNovoNome.Text = string.Empty;
                TxtNovoCpf.Text = string.Empty;
                TxtNovoCep.Text = string.Empty;
                TxtNovoCodigoCasa.Text = string.Empty;
                TxtNovoNumero.Text = string.Empty;

                await CarregarDadosDashboard();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro ao Salvar", ex.Message, "OK");
            }
        }

        // === EDIÇÃO DE DONO ===
        private async void BtnEditarDono_Clicked(object sender, EventArgs e)
        {
            if (_db == null) return;

            var button = sender as Button;
            var dono = button?.CommandParameter as DonoModel;
            if (dono == null) return;

            string novoNome = await DisplayPromptAsync("Editar Dono", "Informe o novo nome:", initialValue: dono.Nome);
            if (string.IsNullOrWhiteSpace(novoNome)) return;

            string novoNumero = await DisplayPromptAsync("Editar Dono", "Informe o novo número/complemento:", initialValue: dono.Numres);
            if (string.IsNullOrWhiteSpace(novoNumero)) return;

            try
            {
                DocumentReference docRef = _db.Collection("Usuarios").Document(dono.Cpf);
                Dictionary<string, object> atualizacao = new Dictionary<string, object>
                {
                    { "nome", novoNome.Trim() },
                    { "numres", novoNumero.Trim() }
                };

                await docRef.UpdateAsync(atualizacao);
                await DisplayAlert("Sucesso", "Dados do responsável atualizados!", "OK");
                await CarregarDadosDashboard();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", $"Falha ao atualizar dados: {ex.Message}", "OK");
            }
        }

        private async void BtnRemoverDono_Clicked(object sender, EventArgs e)
        {
            if (_db == null) return;

            var botao = sender as Button;
            string? cpfDonoParaRemover = botao?.CommandParameter as string;

            if (string.IsNullOrWhiteSpace(cpfDonoParaRemover)) return;

            bool confirmar = await DisplayAlert("Excluir Dono", $"Tem certeza de que deseja remover o Dono com CPF {cpfDonoParaRemover}?", "Sim, Deletar", "Cancelar");

            if (confirmar)
            {
                try
                {
                    DocumentReference docRef = _db.Collection("Usuarios").Document(cpfDonoParaRemover);
                    await docRef.DeleteAsync();

                    await DisplayAlert("Sucesso", "Dono de Casa removido do sistema.", "OK");
                    await CarregarDadosDashboard();
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Erro ao Remover", "Não foi possível remover o registro: " + ex.Message, "OK");
                }
            }
        }

        private async void BtnAdicionarMorador_Clicked(object sender, EventArgs e)
        {
            if (_db == null) return;

            var botao = sender as Button;
            var donoSelecionado = botao?.CommandParameter as DonoModel;

            if (donoSelecionado == null) return;

            string nomeMorador = await DisplayPromptAsync("Novo Morador", $"Adicionar morador na residência de {donoSelecionado.Nome}:");
            if (string.IsNullOrWhiteSpace(nomeMorador)) return;

            string emailMorador = await DisplayPromptAsync("Novo Morador", "Digite o E-mail do morador:", keyboard: Keyboard.Email);
            if (string.IsNullOrWhiteSpace(emailMorador)) return;

            string cpfInput = await DisplayPromptAsync("Novo Morador", "Digite o CPF do morador (Apenas números):", keyboard: Keyboard.Numeric);
            string cpfMoradorLimpo = Regex.Replace(cpfInput ?? "", @"[^\d]", "");

            if (cpfMoradorLimpo.Length != 11)
            {
                await DisplayAlert("Erro", "O CPF deve conter exatamente 11 dígitos.", "OK");
                return;
            }

            try
            {
                DocumentReference moradorRef = _db.Collection("Usuarios")
                                                   .Document(donoSelecionado.Cpf)
                                                   .Collection("Moradores")
                                                   .Document(cpfMoradorLimpo);

                Dictionary<string, object> dadosMorador = new Dictionary<string, object>
                {
                    { "nome", nomeMorador.Trim() },
                    { "cpf", cpfMoradorLimpo },
                    { "email", emailMorador.Trim().ToLower() },
                    { "cargo", "morador" }
                };

                await moradorRef.SetAsync(dadosMorador);

                await DisplayAlert("Sucesso", $"Morador {nomeMorador} adicionado com e-mail cadastrado!", "OK");
                await CarregarDadosDashboard();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", "Falha ao vincular morador: " + ex.Message, "OK");
            }
        }

        // === EDIÇÃO DE MORADOR ===
        private async void BtnEditarMorador_Clicked(object sender, EventArgs e)
        {
            if (_db == null) return;

            var button = sender as Button;
            var morador = button?.CommandParameter as MoradorModel;
            if (morador == null) return;

            var parentView = button?.Parent as Element;
            DonoModel? donoPai = null;

            while (parentView != null)
            {
                if (parentView.BindingContext is DonoModel dono)
                {
                    donoPai = dono;
                    break;
                }
                parentView = parentView.Parent;
            }

            if (donoPai == null || string.IsNullOrWhiteSpace(donoPai.Cpf))
            {
                await DisplayAlert("Erro", "Não foi possível localizar o Dono associado a este morador.", "OK");
                return;
            }

            string novoNome = await DisplayPromptAsync("Editar Morador", "Informe o novo nome:", initialValue: morador.Nome);
            if (string.IsNullOrWhiteSpace(novoNome)) return;

            string novoEmail = await DisplayPromptAsync("Editar Morador", "Informe o novo e-mail:", initialValue: morador.Email, keyboard: Keyboard.Email);
            if (string.IsNullOrWhiteSpace(novoEmail)) return;

            try
            {
                DocumentReference moradorRef = _db.Collection("Usuarios")
                                                  .Document(donoPai.Cpf)
                                                  .Collection("Moradores")
                                                  .Document(morador.Cpf);

                Dictionary<string, object> atualizacao = new Dictionary<string, object>
                {
                    { "nome", novoNome.Trim() },
                    { "email", novoEmail.Trim().ToLower() }
                };

                await moradorRef.UpdateAsync(atualizacao);
                await DisplayAlert("Sucesso", "Morador atualizado com sucesso!", "OK");
                await CarregarDadosDashboard();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", $"Erro ao editar morador: {ex.Message}", "OK");
            }
        }

        private async void BtnRemoverMorador_Clicked(object sender, EventArgs e)
        {
            if (_db == null) return;

            var button = sender as Button;
            var morador = button?.CommandParameter as MoradorModel;

            if (morador == null) return;

            var parentView = button?.Parent as Element;
            DonoModel? donoPai = null;

            while (parentView != null)
            {
                if (parentView.BindingContext is DonoModel dono)
                {
                    donoPai = dono;
                    break;
                }
                parentView = parentView.Parent;
            }

            if (donoPai == null || string.IsNullOrWhiteSpace(donoPai.Cpf))
            {
                await DisplayAlert("Erro", "Não foi possível localizar o Dono associado a este morador.", "OK");
                return;
            }

            bool confirmar = await DisplayAlert("Confirmação", $"Deseja remover o morador {morador.Nome}?", "Sim", "Não");
            if (!confirmar) return;

            try
            {
                DocumentReference moradorRef = _db.Collection("Usuarios")
                                                  .Document(donoPai.Cpf)
                                                  .Collection("Moradores")
                                                  .Document(morador.Cpf);

                await moradorRef.DeleteAsync();

                await DisplayAlert("Sucesso", "Morador removido com sucesso!", "OK");
                await CarregarDadosDashboard();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", $"Erro ao remover morador: {ex.Message}", "OK");
            }
        }

        private async void BtnSair_Clicked(object sender, EventArgs e)
        {
            bool confirmarSair = await DisplayAlert("Desconectar", "Tem certeza de que deseja fechar a sessão administrativa?", "Sim, Sair", "Cancelar");

            if (confirmarSair)
            {
                try
                {
                    if (Application.Current != null)
                    {
                        Application.Current.MainPage = new NavigationPage(new bemvindo());
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Erro", "Não foi possível sair: " + ex.Message, "OK");
                }
            }
        }

        private async void OnIniciarCameraClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new CameraPage());
        }
    }
}