using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Maui.Controls;

namespace SegurançaRe
{
    public partial class CameraPage : ContentPage
    {
        public CameraPage()
        {
            InitializeComponent();
        }

        private void OnIniciarCameraClicked(object sender, EventArgs e)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string pastaScript = Path.Combine(desktopPath, "app_yolo.py");
                string caminhoArquivoPy = Path.Combine(pastaScript, "app_yolo.py");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{caminhoArquivoPy}\"",
                    WorkingDirectory = pastaScript,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);

                if (LblStatus != null)
                    LblStatus.Text = "Câmera iniciada com sucesso em uma nova janela.";
            }
            catch (Exception ex)
            {
                DisplayAlert("Erro", $"Falha ao executar o script Python: {ex.Message}", "OK");
            }
        }
    }
}