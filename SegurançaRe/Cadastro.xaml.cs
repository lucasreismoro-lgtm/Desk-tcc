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
   
    // Classe lógica parcial que controla a interface gráfica de Cadastro/Login de Administradores.
    
    public partial class Cadastro : ContentPage
    {
        // 1. DEFINE A SENHA GLOBAL QUE OS 3 ADMINS CONHECEM (Chave mestra estática)
        private const string SENHA_GLOBAL_MASTER = "safehome3admins";

        // Declaração do objeto de conexão com o banco de dados Cloud Firestore do Firebase
        private FirestoreDb _db;

        
        // Construtor da classe de cadastro. Inicializa os elementos visuais configurados no XAML.
        
        public Cadastro()
        {
            InitializeComponent();
        }

      
        //Método assíncrono disparado automaticamente pelo ciclo de vida do MAUI assim que a tela se torna visível.
       
        protected override async void OnAppearing()
        {
            // Executa o comportamento padrão de exibição da classe base
            base.OnAppearing();

            // Inicia de forma assíncrona o processo de conexão com o banco de dados
            await InicializarFirebase();
        }

       
        // Método assíncrono responsável por buscar as credenciais locais e instanciar o objeto do Firestore.
        
        private async Task InicializarFirebase()
        {
            // Se o objeto de conexão já existe, evita realizar o processo de inicialização novamente
            if (_db != null) return;

            try
            {
                // Variável para armazenar o conteúdo de texto cru das credenciais de acesso
                string jsonConteudo = string.Empty;

                try
                {
                    // Tenta abrir o arquivo conexao.json embutido nos arquivos de compilação do aplicativo (Assets/Raw)
                    using var stream = await FileSystem.OpenAppPackageFileAsync("conexao.json");
                    using var reader = new StreamReader(stream);
                    jsonConteudo = await reader.ReadToEndAsync();
                }
                catch (FileNotFoundException)
                {
                    //  Caso o arquivo não esteja no pacote compilado, busca no diretório do projeto
                    string pastaProjeto = AppDomain.CurrentDomain.BaseDirectory;

                    // Reconstrói o caminho de pastas físicas até a pasta Resources/Raw onde o arquivo original reside
                    string caminhoAlternativo = Path.Combine(pastaProjeto, "..", "..", "..", "..", "..", "Resources", "Raw", "conexao.json");

                    // Verifica se o arquivo foi localizado com sucesso no caminho alternativo do disco
                    if (File.Exists(caminhoAlternativo))
                    {
                        // Lê todo o conteúdo textual do arquivo JSON de forma assíncrona
                        jsonConteudo = await File.ReadAllTextAsync(caminhoAlternativo);
                    }
                    else
                    {
                        // Lança um erro customizado se o arquivo estiver ausente em todas as rotas possíveis
                        throw new Exception("Arquivo conexao.json não encontrado nos caminhos padrão do app.");
                    }
                }

                // Configura o inicializador do banco usando o ID do projeto no console do Firebase e o JSON de credenciais
                FirestoreDbBuilder builder = new FirestoreDbBuilder
                {
                    ProjectId = "banco-tcc-dc633",
                    JsonCredentials = jsonConteudo
                };

                // Compila a configuração e injeta a instância de conexão ativa na variável de controle
                _db = builder.Build();
            }
            catch (Exception ex)
            {
                // Exibe uma caixa de mensagem informando o administrador sobre a falha de comunicação física com o banco
                await DisplayAlert("Erro Firebase", "Falha ao inicializar o banco: " + ex.Message, "OK");
            }
        }

        
        // Evento acionado ao clicar no botão de login, executando as 4 barreiras de validação de acesso.
       
        private async void BtnLoginAdmin_Clicked(object sender, EventArgs e)
        {
            //  Se o banco de dados não terminou de se conectar, impede a tentativa de login
            if (_db == null)
            {
                await DisplayAlert("Aguarde", "O sistema ainda está conectando ao banco de dados. Tente novamente em instantes.", "OK");
                return;
            }

            // Resgata e limpa os espaços inúteis no início e fim d e todos os textos digitados nos campos
            string nomeDigitado = TxtNomeAdmin.Text?.Trim();
            string cpfDigitadoRaw = TxtCpfAdmin.Text?.Trim();
            string senhaPessoalDigitada = TxtSenhaPessoal.Text?.Trim();
            string senhaGlobalDigitada = TxtSenhaGlobal.Text?.Trim();

            // Camada 1: Validação de preenchimento. Garante que nenhum campo obrigatório esteja nulo ou vazio
            if (string.IsNullOrEmpty(nomeDigitado) || string.IsNullOrEmpty(cpfDigitadoRaw) ||
                string.IsNullOrEmpty(senhaPessoalDigitada) || string.IsNullOrEmpty(senhaGlobalDigitada))
            {
                await DisplayAlert("Campos Vazios", "Todos os campos do administrador são obrigatórios.", "OK");
                return;
            }

            // Expressão regular (Regex) para remover todos os caracteres não numéricos (pontos, traços, letras) do CPF
            string cpfLimpo = Regex.Replace(cpfDigitadoRaw, @"[^\d]", "");

            // Valida se o CPF restante tem exatamente o tamanho exigido pelo padrão brasileiro de identificação
            if (cpfLimpo.Length != 11)
            {
                await DisplayAlert("CPF Inválido", "O CPF do administrador deve conter 11 dígitos.", "OK");
                return;
            }

            // Camada 2: Validação da chave mestre global comparando a entrada com a constante do sistema
            if (senhaGlobalDigitada != SENHA_GLOBAL_MASTER)
            {
                await DisplayAlert("Acesso Bloqueado", "Senha Global do Sistema Incorreta. Chave mestra inválida.", "OK");
                return;
            }

            try
            {
                // Camada 3: Cria um apontamento para o documento do Firestore identificado pelo CPF na tabela de Administradores
                DocumentReference docRef = _db.Collection("Administradores").Document(cpfLimpo);

                // Realiza a leitura assíncrona do registro do administrador diretamente nos servidores do Firebase
                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

                // Verifica se o CPF pesquisado existe na base de dados de administradores cadastrados
                if (snapshot.Exists)
                {
                    // Recupera o valor textual contido na propriedade "senhaPessoal" guardada no documento do banco
                    string senhaSalvaNoBanco = snapshot.GetValue<string>("senhaPessoal");

                    // Camada 4: Confere se a senha pessoal digitada bate exatamente com a senha pessoal persistida no banco
                    if (senhaPessoalDigitada == senhaSalvaNoBanco)
                    {
                        // Exibe mensagem de validação total das credenciais e boas-vindas ao painel
                        await DisplayAlert("Autenticação Concluída ", $"Bem-vindo ao Painel de Controle Master, Admin {nomeDigitado}!", "OK");

                        // Navega dinamicamente no Shell do MAUI para carregar a tela principal de controle de usuários
                        await Shell.Current.GoToAsync("//TelaUsuarioPage");
                    }
                    else
                    {
                        // Alerta caso a senha pessoal esteja incorreta
                        await DisplayAlert("Acesso Recusado", "Senha Pessoal incorreta para este Administrador.", "OK");
                    }
                }
                else
                {
                    // Alerta caso o CPF não faça parte do grupo de administradores cadastrados na nuvem
                    await DisplayAlert("Acesso Negado", "Este CPF não consta na base de dados de Administradores do sistema.", "OK");
                }
            }
            catch (Exception ex)
            {
                // Captura e apresenta erros técnicos (como queda de internet ou regras de segurança do Firestore violadas)
                await DisplayAlert("Erro de Autenticação", "Erro ao conectar no Firestore Master: " + ex.Message, "OK");
            }
        }

        
        // Evento acionado ao clicar no botão "Limpar" na tela, limpando todas as entradas visuais do operador.
        
        private void BtnLimpar_Clicked(object sender, EventArgs e)
        {
            TxtNomeAdmin.Text = string.Empty;
            TxtCpfAdmin.Text = string.Empty;
            TxtSenhaPessoal.Text = string.Empty;
            TxtSenhaGlobal.Text = string.Empty;
        }
    }
}