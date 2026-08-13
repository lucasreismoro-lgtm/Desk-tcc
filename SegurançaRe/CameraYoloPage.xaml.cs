using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SegurançaRe
{
    public partial class CameraYoloPage : ContentPage
    {
        private InferenceSession? _onnxSession;
        private bool _isProcessingFrame = false;

        // Dimensões padrão exigidas pelo YOLOv8 / YOLOv11
        private const int TargetWidth = 640;
        private const int TargetHeight = 640;

        public CameraYoloPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CarregarModeloOnnx();
        }

        // ================= 1. CARREGAR O MODELO .ONNX =================
        private async Task CarregarModeloOnnx()
        {
            try
            {
                // Carrega o arquivo do modelo a partir da pasta Resources/Raw
                using var stream = await FileSystem.OpenAppPackageFileAsync("yolov8n.onnx");
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);

                // Inicializa a sessão do ONNX Runtime
                _onnxSession = new InferenceSession(memoryStream.ToArray());

                if (LblStatus != null)
                    LblStatus.Text = "YOLO Carregado com Sucesso!";
            }
            catch (Exception ex)
            {
                if (LblStatus != null)
                    LblStatus.Text = $"Erro ao carregar modelo ONNX: {ex.Message}";

                System.Diagnostics.Debug.WriteLine($"Erro ONNX: {ex.Message}");
            }
        }

        // ================= 2. PROCESSAR O FRAME DA CÂMERA =================
        private async void OnMediaCaptured(object sender, CommunityToolkit.Maui.Views.MediaCapturedEventArgs e)
        {
            if (_onnxSession == null || _isProcessingFrame) return;

            _isProcessingFrame = true;

            try
            {
                // Usa o Stream vindo do evento do CommunityToolkit
                using var imageStream = e.Media;
                if (imageStream == null) return;

                var deteccoes = await Task.Run(() => ExecutarInferenciaYolo(imageStream));

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DesenharDeteccoes(deteccoes);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro no processamento do frame: {ex.Message}");
            }
            finally
            {
                _isProcessingFrame = false;
            }
        }

        // ================= 3. INFERÊNCIA COM ONNX RUNTIME =================
        private List<DeteccaoYolo> ExecutarInferenciaYolo(Stream streamImagem)
        {
            var listaDeteccoes = new List<DeteccaoYolo>();

            if (_onnxSession == null) return listaDeteccoes;

            // Transforma a imagem no Tensor de entrada (1, 3, 640, 640)
            var inputTensor = CriarTensorDaImagem(streamImagem);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            // Executa o modelo
            using var results = _onnxSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Processa as saídas da matriz (Bounding Boxes e Confiança)
            listaDeteccoes = ProcessarSaidaYolo(output);

            return listaDeteccoes;
        }

        // ================= 4. PRÉ-PROCESSAMENTO DA IMAGEM =================
        private DenseTensor<float> CriarTensorDaImagem(Stream stream)
        {
            // Cria o formato de entrada [BatchSize, Canais RGB, Altura, Largura]
            var tensor = new DenseTensor<float>(new[] { 1, 3, TargetHeight, TargetWidth });

            // Nota: Os valores dos pixels RGB (0 a 255) devem ser normalizados entre 0.0f e 1.0f
            return tensor;
        }

        // ================= 5. PÓS-PROCESSAMENTO DAS CAIXAS =================
        private List<DeteccaoYolo> ProcessarSaidaYolo(Tensor<float> output)
        {
            var resultados = new List<DeteccaoYolo>();

            // Limiar de confiança mínimo (ex: 50%)
            float minConfidence = 0.5f;

            // O formato padrão de saída do YOLOv8 é [1, 84, 8400]
            // Onde 84 = [x, y, w, h + 80 classes]
            int dimensions = output.Dimensions[1];
            int anchors = output.Dimensions[2];

            for (int i = 0; i < anchors; i++)
            {
                float maxScore = 0;
                int classId = -1;

                // Procura a classe com maior pontuação para esta caixa
                for (int c = 4; c < dimensions; c++)
                {
                    float score = output[0, c, i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        classId = c - 4;
                    }
                }

                if (maxScore >= minConfidence)
                {
                    float x = output[0, 0, i];
                    float y = output[0, 1, i];
                    float w = output[0, 2, i];
                    float h = output[0, 3, i];

                    resultados.Add(new DeteccaoYolo
                    {
                        X = x - (w / 2),
                        Y = y - (h / 2),
                        Largura = w,
                        Altura = h,
                        Confianca = maxScore,
                        ClasseId = classId
                    });
                }
            }

            return resultados;
        }

        // ================= 6. DESENHAR RETÂNGULOS NA TELA =================
        private void DesenharDeteccoes(List<DeteccaoYolo> deteccoes)
        {
            if (graphicsView == null) return;

            // Atualiza a camada gráfica por cima do feed da câmera
            graphicsView.Drawable = new YoloDrawable(deteccoes);
            graphicsView.Invalidate();

            if (LblStatus != null)
            {
                LblStatus.Text = deteccoes.Count > 0
                    ? $"Objetos Detectados: {deteccoes.Count}"
                    : "Procurando objetos...";
            }
        }
    }

    // Estrutura para salvar cada detecção
    public class DeteccaoYolo
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Largura { get; set; }
        public float Altura { get; set; }
        public float Confianca { get; set; }
        public int ClasseId { get; set; }
    }

    // Desenha as caixas na tela usando Microsoft.Maui.Graphics
    public class YoloDrawable : IDrawable
    {
        private readonly List<DeteccaoYolo> _deteccoes;

        public YoloDrawable(List<DeteccaoYolo> deteccoes)
        {
            _deteccoes = deteccoes;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = Colors.Green;
            canvas.StrokeSize = 3;
            canvas.FontColor = Colors.White;
            canvas.FontSize = 14;

            foreach (var item in _deteccoes)
            {
                // Converte as coordenadas do YOLO (640x640) para a resolução da tela
                float scaleX = dirtyRect.Width / 640f;
                float scaleY = dirtyRect.Height / 640f;

                float rx = item.X * scaleX;
                float ry = item.Y * scaleY;
                float rw = item.Largura * scaleX;
                float rh = item.Altura * scaleY;

                // Desenha a caixa delimitadora
                canvas.DrawRectangle(rx, ry, rw, rh);

                // Rótulo da classe com a porcentagem
                string label = $"Classe {item.ClasseId}: {item.Confianca * 100:F0}%";

                // Desenha o texto formatado acima da caixa
                canvas.DrawString(label, rx, ry - 20, rw, 20, HorizontalAlignment.Left, VerticalAlignment.Top);
            }
        }
    }
}