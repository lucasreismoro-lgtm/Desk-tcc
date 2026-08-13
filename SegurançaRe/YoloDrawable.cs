using Microsoft.Maui.Graphics;

namespace SegurançaRe
{
    // Estrutura para guardar o resultado do YOLO
    public class BoundingBox
    {
        public RectF Bounds { get; set; }
        public string Label { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }

    public class YoloDrawable : IDrawable
    {
        public List<BoundingBox> Boxes { get; set; } = new();

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Boxes == null || Boxes.Count == 0) return;

            canvas.StrokeSize = 3;
            canvas.StrokeColor = Colors.Red;
            canvas.FontColor = Colors.White;
            canvas.FontSize = 14;

            foreach (var box in Boxes)
            {
                // 1. Desenha o retângulo em volta do objeto
                canvas.DrawRectangle(box.Bounds);

                // 2. Desenha o fundo da tag com o nome
                string labelText = $"{box.Label} ({box.Confidence:P0})";
                canvas.FillColor = Colors.Red;
                canvas.FillRectangle(box.Bounds.X, box.Bounds.Y - 20, 150, 20);

                // 3. Desenha o texto do nome (Ajustado para a assinatura correta do DrawString)
                canvas.DrawString(labelText, box.Bounds.X + 5, box.Bounds.Y - 5, HorizontalAlignment.Left);
            }
        }
    }
}