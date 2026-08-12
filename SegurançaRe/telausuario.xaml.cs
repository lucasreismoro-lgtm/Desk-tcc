using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Google.Cloud.Firestore;

namespace SegurançaRe
{
    // === CLASSES DE MODELO (MODELS / ESTRUTURA DE DADOS) ===

    public class DonoModel // Modelo que representa o Dono de Casa/Responsável principal
    {
        public string Nome { get; set; } = string.Empty; // Nome completo do responsável
        public string Cpf { get; set; } = string.Empty; // CPF do dono (usado como ID no Firestore)
        public string IdResidencia { get; set; } = string.Empty; // Código identificador da residência
        public string Cep { get; set; } = string.Empty; // CEP do imóvel
        public string Numres { get; set; } = string.Empty; // Número ou complemento do imóvel
        public List<MoradorModel> Moradores { get; set; } = new List<MoradorModel>(); // Lista de dependentes vinculados

        public string DetalhesResidencia => $"Residência: {IdResidencia} | Nº: {Numres} | CEP: {Cep}"; // Texto formatado da residência
        public string CpfFormatado => string.IsNullOrWhiteSpace(Cpf) ? "" : $"CPF: {Cpf}"; // CPF formatado para a UI
        public string TotalMoradoresTexto => $"Moradores cadastrados: {Moradores.Count}"; // Contador formatado de moradores
    }

    public class MoradorModel // Modelo que representa cada morador dependente
    {
        public string Nome { get; set; } = string.Empty; // Nome completo do morador dependente
        public string Cpf { get; set; } = string.Empty; // CPF numérico do dependente
        public string Email { get; set; } = string.Empty; // E-mail cadastrado do morador dependente
        public string Cargo { get; set; } = "morador"; // Privilégio fixo do dependente no sistema
    }

    public class LogModel // Modelo para auditoria de eventos
    {
        public string Horario { get; set; } = string.Empty; // Data e hora do registro
        public string Usuario { get; set; } = string.Empty; // Identificador do autor do evento
        public string Acao { get; set; } = string.Empty; // Descrição da ação executada
    }

    // CÓDIGO LÓGICO DA TELA (CODE-BEHIND / CONTROLADOR)

    public partial class telausuario : ContentPage // Controlador associado ao layout XAML
    {
        private FirestoreDb? _db; // Instância de conexão do banco Firestore

        public telausuario() // Construtor da página
        {
            InitializeComponent(); // Carrega os componentes gráficos da interface
        }

        protected override async void OnAppearing() // Evento disparado ao abrir a tela
        {
            base.OnAppearing(); // Mantém o comportamento base
            await InicializarFirebaseEALeitura(); // Conecta no banco e lê os dados
        }

        private async Task InicializarFirebaseEALeitura() // Inicializa as credenciais do Firestore
        {
            if (_db == null) // Executa apenas se ainda não houver conexão ativa
            {
                try
                {
                    string jsonConteudo = string.Empty; // Variável auxiliar para o JSON de chave

                    try
                    {
                        using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json"); // Tenta abrir o JSON nos arquivos empacotados
                        using var reader = new StreamReader(stream); // Prepara o leitor de texto
                        jsonConteudo = await reader.ReadToEndAsync(); // Lê todo o conteúdo do JSON
                    }
                    catch (FileNotFoundException)
                    {
                        string pastaProjeto = AppDomain.CurrentDomain.BaseDirectory; // Pega o diretório do binário
                        string caminhoAlternativo = Path.Combine(pastaProjeto, "..", "..", "..", "..", "..", "Resources", "Raw", "conexao.json"); // Caminho relativo de fallback

                        if (File.Exists(caminhoAlternativo)) // Se achar no caminho do projeto local
                        {
                            jsonConteudo = await File.ReadAllTextAsync(caminhoAlternativo); // Lê o arquivo físico
                        }
                    }

                    if (!string.IsNullOrEmpty(jsonConteudo)) // Se as credenciais foram carregadas
                    {
                        FirestoreDbBuilder builder = new FirestoreDbBuilder // Configura o construtor da API
                        {
                            ProjectId = "banco-tcc-dc633", // ID do projeto no Firebase
                            JsonCredentials = jsonConteudo // Injeta a chave pública/privada JSON
                        };
                        _db = builder.Build(); // Conclui a construção da instância
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Erro de Conexão", "Não foi possível carregar as credenciais: " + ex.Message, "OK"); // Alerta de erro no JSON
                    return; // Interrompe a execução
                }
            }

            await CarregarDadosDashboard(); // Alimenta a interface gráfica
        }

        private async Task CarregarDadosDashboard() // Consulta no banco e renderiza os dados na tela
        {
            if (_db == null) return; // Aborta se o banco não estiver disponível

            try
            {
                Query usuariosQuery = _db.Collection("Usuarios").WhereEqualTo("cargo", "dono_da_casa"); // Query filtrando apenas donos de casa
                QuerySnapshot usuariosSnapshot = await usuariosQuery.GetSnapshotAsync(); // Puxa os dados atualizados

                List<DonoModel> listaDonos = new List<DonoModel>(); // Lista que armazenará as models para a UI

                foreach (DocumentSnapshot document in usuariosSnapshot.Documents) // Percorre cada dono encontrado
                {
                    if (document.Exists) // Garante que o documento é válido
                    {
                        document.TryGetValue("nome", out string nomeDono); // Extrai o nome do dono
                        document.TryGetValue("cpf", out string cpfDono); // Extrai o CPF do dono
                        document.TryGetValue("id_residencia", out string idRes); // Extrai o ID da residência
                        document.TryGetValue("cep", out string cepDono); // Extrai o CEP
                        document.TryGetValue("numres", out string numresDono); // Extrai o número do imóvel

                        var novoDono = new DonoModel // Monta a model do Dono
                        {
                            Nome = nomeDono ?? "Sem Nome", // Trata nulos com valor padrão
                            Cpf = cpfDono ?? document.Id, // Fallback para a chave primária
                            IdResidencia = idRes ?? "N/A", // Trata nulos
                            Cep = cepDono ?? "N/A", // Trata nulos
                            Numres = numresDono ?? "N/A" // Trata nulos
                        };

                        try
                        {
                            QuerySnapshot moradoresSnapshot = await document.Reference.Collection("Moradores").GetSnapshotAsync(); // Busca subcoleção Moradores

                            foreach (DocumentSnapshot moradorDoc in moradoresSnapshot.Documents) // Itera os moradores do dono
                            {
                                if (moradorDoc.Exists)
                                {
                                    moradorDoc.TryGetValue("nome", out string nomeMorador); // Extrai nome do morador
                                    moradorDoc.TryGetValue("cpf", out string cpfMorador); // Extrai CPF do morador
                                    moradorDoc.TryGetValue("email", out string emailMorador); // Extrai o E-mail do morador

                                    novoDono.Moradores.Add(new MoradorModel // Adiciona morador na lista interna do Dono
                                    {
                                        Nome = nomeMorador ?? "Morador sem Nome", // Valor padrão se nulo
                                        Cpf = cpfMorador ?? moradorDoc.Id, // Fallback do CPF
                                        Email = emailMorador ?? "Sem E-mail" // Resgata o e-mail ou fallback
                                    });
                                }
                            }
                        }
                        catch { /* Subcoleção sem documentos registrados */ }

                        listaDonos.Add(novoDono); // Insere o dono estruturado na lista
                    }
                }

                ListViewUsuarios.ItemsSource = null; // Zera a coleção da lista para atualizar o layout
                ListViewUsuarios.ItemsSource = listaDonos; // Vincula a lista populada à UI
                LblTotalUsuarios.Text = listaDonos.Count.ToString("D2"); // Formata e atualiza a métrica em 2 dígitos
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro de Sincronização", "Erro ao processar estrutura do Firestore: " + ex.Message, "OK"); // Notifica falhas de consulta
            }
        }

        private async void BtnAtualizarDados_Clicked(object sender, EventArgs e) // Clique do botão Recarregar
        {
            await CarregarDadosDashboard(); // Força nova busca na nuvem
        }

        private async void BtnNovoDono_Clicked(object sender, EventArgs e) // Cadastro de novo Dono + Subcoleções
        {
            if (_db == null) return; // Cancela se sem conexão

            string nome = TxtNovoNome.Text?.Trim(); // Trata o texto do nome
            string cpfRaw = TxtNovoCpf.Text?.Trim(); // Trata o texto do CPF
            string cepRaw = TxtNovoCep.Text?.Trim(); // Trata o texto do CEP
            string codigoCasa = TxtNovoCodigoCasa.Text?.Trim(); // Trata o código da residência
            string numeroCasa = TxtNovoNumero.Text?.Trim(); // Trata o número do imóvel

            if (string.IsNullOrEmpty(nome) || string.IsNullOrEmpty(cpfRaw) ||
                string.IsNullOrEmpty(cepRaw) || string.IsNullOrEmpty(codigoCasa) || string.IsNullOrEmpty(numeroCasa))
            {
                await DisplayAlert("Aviso", "Preencha todos os campos do formulário.", "OK"); // Valida preenchimento total
                return;
            }

            string cpfLimpo = Regex.Replace(cpfRaw, @"[^\d]", ""); // Filtra e deixa apenas números no CPF
            string cepLimpo = Regex.Replace(cepRaw, @"[^\d]", ""); // Filtra e deixa apenas números no CEP

            if (cpfLimpo.Length != 11) // Validação de dígitos do CPF
            {
                await DisplayAlert("CPF Inválido", "O CPF deve conter 11 dígitos.", "OK"); // Alerta regra do CPF
                return;
            }

            try
            {
                Query queryCodigo = _db.Collection("Usuarios").WhereEqualTo("id_residencia", codigoCasa); // Consulta unicidade de casa
                QuerySnapshot checkCodigo = await queryCodigo.GetSnapshotAsync(); // Executa a verificação

                if (checkCodigo.Documents.Count > 0) // Impede código duplicado
                {
                    await DisplayAlert("Código Indisponível", $"O código '{codigoCasa}' já está sendo utilizado por outra residência.", "OK"); // Alerta duplicidade
                    return;
                }

                DocumentReference docRef = _db.Collection("Usuarios").Document(cpfLimpo); // Referência com chave no CPF do Dono

                Dictionary<string, object> novoUsuario = new Dictionary<string, object> // Dicionário do Dono da Casa
                {
                    { "nome", nome }, // Nome completo
                    { "cpf", cpfLimpo }, // CPF limpo
                    { "cargo", "dono_da_casa" }, // Cargo do dono
                    { "id_residencia", codigoCasa }, // ID da casa
                    { "cep", cepLimpo }, // CEP sem pontuação
                    { "numres", numeroCasa } // Número do imóvel
                };

                await docRef.SetAsync(novoUsuario); // Grava os dados do Dono no Firestore

                // ====== 1. CRIAÇÃO AUTOMÁTICA DA SUBCOLEÇÃO "Sensores" ======
                DocumentReference sensoresRef = docRef.Collection("Sensores").Document("estado"); // Caminho: Usuarios/{cpf}/Sensores/estado

                Dictionary<string, object> estadoInicialSensores = new Dictionary<string, object> // Estado padrão inicial dos switches
                {
                    { "presencaAtivo", false }, // Sensor de presença desligado por padrão
                    { "calorAtivo", false },    // Sensor de calor desligado por padrão
                    { "alarmeAtivo", false }    // Alarme sonoro desligado por padrão
                };

                await sensoresRef.SetAsync(estadoInicialSensores); // Grava documento dos sensores

                // ====== 2. CRIAÇÃO AUTOMÁTICA DA SUBCOLEÇÃO "Historico" ======
                DocumentReference historicoRef = docRef.Collection("Historico").Document(); // Caminho: Usuarios/{cpf}/Historico/{ID_GERADO_AUTO}

                Dictionary<string, object> eventoInicialHistorico = new Dictionary<string, object>
                {
                    { "tipo", "SISTEMA" },
                    { "mensagem", "Sistema de segurança e registro de histórico inicializados." },
                    { "dataHora", FieldValue.ServerTimestamp } // Data e hora atual do servidor Firestore
                };

                await historicoRef.SetAsync(eventoInicialHistorico); // Grava primeiro registro de histórico

                await DisplayAlert("Sucesso", $"Dono {nome} cadastrado com sucesso! Estruturas de Sensores e Histórico inicializadas.", "OK"); // Alerta confirmação

                TxtNovoNome.Text = string.Empty; // Limpa os campos de texto
                TxtNovoCpf.Text = string.Empty;
                TxtNovoCep.Text = string.Empty;
                TxtNovoCodigoCasa.Text = string.Empty;
                TxtNovoNumero.Text = string.Empty;

                await CarregarDadosDashboard(); // Recarrega a lista
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro ao Salvar", ex.Message, "OK"); // Captura erros no cadastro
            }
        }

        private async void BtnAdicionarMorador_Clicked(object sender, EventArgs e) // Cadastro de Morador dependente + E-mail
        {
            if (_db == null) return; // Aborta sem banco

            var botao = (Button)sender; // Mapeia o botão clicado
            var donoSelecionado = botao.CommandParameter as DonoModel; // Captura a model do dono correspondente ao botão

            if (donoSelecionado == null) return;

            string nomeMorador = await DisplayPromptAsync("Novo Morador", $"Adicionar morador na residência de {donoSelecionado.Nome}:"); // Prompt do Nome
            if (string.IsNullOrWhiteSpace(nomeMorador)) return; // Aborta se vazio

            string emailMorador = await DisplayPromptAsync("Novo Morador", "Digite o E-mail do morador:", keyboard: Keyboard.Email); // Prompt do E-mail
            if (string.IsNullOrWhiteSpace(emailMorador)) return; // Aborta se vazio

            string cpfInput = await DisplayPromptAsync("Novo Morador", "Digite o CPF do morador (Apenas números):", keyboard: Keyboard.Numeric); // Prompt do CPF
            string cpfMoradorLimpo = Regex.Replace(cpfInput ?? "", @"[^\d]", ""); // Higieniza o CPF

            if (cpfMoradorLimpo.Length != 11) // Valida tamanho do CPF
            {
                await DisplayAlert("Erro", "O CPF deve conter exatamente 11 dígitos.", "OK"); // Notifica falha do CPF
                return;
            }

            try
            {
                DocumentReference moradorRef = _db.Collection("Usuarios") // Referência da subcoleção Moradores do Dono selecionado
                                                   .Document(donoSelecionado.Cpf)
                                                   .Collection("Moradores")
                                                   .Document(cpfMoradorLimpo);

                Dictionary<string, object> dadosMorador = new Dictionary<string, object> // Estrutura de dados do Morador
                {
                    { "nome", nomeMorador.Trim() }, // Salva nome limpo
                    { "cpf", cpfMoradorLimpo }, // Salva CPF numérico
                    { "email", emailMorador.Trim().ToLower() }, // Salva o e-mail em minúsculas
                    { "cargo", "morador" } // Perfil fixado
                };

                await moradorRef.SetAsync(dadosMorador); // Salva dependente no Firestore

                await DisplayAlert("Sucesso", $"Morador {nomeMorador} adicionado com e-mail cadastrado!", "OK"); // Confirma gravação
                await CarregarDadosDashboard(); // Recarrega tela
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", "Falha ao vincular morador: " + ex.Message, "OK"); // Alerta de erro no processo
            }
        }

        private async void BtnRemoverDono_Clicked(object sender, EventArgs e) // Remoção física de Dono
        {
            if (_db == null) return; // Aborta se sem banco

            var botao = (Button)sender; // Mapeia o botão
            string? cpfDonoParaRemover = botao.CommandParameter as string; // Resgata o CPF do dono via parâmetro

            if (string.IsNullOrWhiteSpace(cpfDonoParaRemover)) return;

            bool confirmar = await DisplayAlert("Excluir Dono", $"Tem certeza de que deseja remover o Dono com CPF {cpfDonoParaRemover}?", "Sim, Deletar", "Cancelar"); // Confirmação

            if (confirmar)
            {
                try
                {
                    DocumentReference docRef = _db.Collection("Usuarios").Document(cpfDonoParaRemover); // Referência ao documento do dono
                    await docRef.DeleteAsync(); // Executa exclusão no Firestore

                    await DisplayAlert("Sucesso", "Dono de Casa removido do sistema.", "OK"); // Exibe sucesso
                    await CarregarDadosDashboard(); // Recarrega dashboard
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Erro ao Remover", "Não foi possível remover o registro: " + ex.Message, "OK"); // Alerta falha na exclusão
                }
            }
        }

        private async void BtnSair_Clicked(object sender, EventArgs e) // Encerramento de sessão
        {
            bool confirmarSair = await DisplayAlert("Desconectar", "Tem certeza de que deseja fechar a sessão administrativa?", "Sim, Sair", "Cancelar"); // Pop-up de saída

            if (confirmarSair)
            {
                try
                {
                    Application.Current.MainPage = new NavigationPage(new Cadastro()); // Redireciona para a tela inicial/login
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Erro", "Não foi possível retornar para a tela de login: " + ex.Message, "OK"); // Notifica falha na navegação
                }
            }
        }
        private async void BtnRemoverMorador_Clicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            var morador = button?.CommandParameter as MoradorModel;

            if (morador == null) return;

            bool confirmar = await DisplayAlert("Confirmação", $"Deseja remover o morador {morador.Nome}?", "Sim", "Não");
            if (!confirmar) return;

            try
            {
                // Pega o dono (DonoModel) que é o BindingContext do Frame pai
                var parentGrid = button.Parent as Grid;
                var viewCell = parentGrid?.Parent;

                // Exemplo de exclusão no Firestore (Ajuste o caminho conforme o seu serviço do Firebase):
                // await firebaseService.DeletarMoradorAsync(cpfDono, morador.Cpf);

                await DisplayAlert("Sucesso", "Morador removido com sucesso!", "OK");

                // Recarregue a lista para atualizar a tela
                // await CarregarDadosAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", $"Erro ao remover morador: {ex.Message}", "OK");
            }
        }
    }

    private YoloSimulator _simuladorYolo = new YoloSimulator();

        private async void BtnSimularCameraYolo_Clicked(object sender, EventArgs e)
        {
            // Se você tiver uma página dedicada para exibir a câmera:
            // await Navigation.PushAsync(new CameraYoloPage());

            // Ou se quiser iniciar a simulação direta em janela secundária:
            await _simuladorYolo.IniciarSimulacaoAsync();
        }
    }