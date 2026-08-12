using System;
using Microsoft.Maui.Controls;

namespace SegurançaRe
{
    public partial class bemvindo : ContentPage
    {
        public bemvindo()
        {
            InitializeComponent();
        }

        private async void BtnComeçar_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//CadastroPage");
        }

        private async void BtnJaTenhoConta_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//CadastroPage");
        }
    }
}
