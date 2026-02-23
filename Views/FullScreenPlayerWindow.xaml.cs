using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibVLCSharp.WPF;

namespace AtlasHub.Views
{
    public partial class FullScreenPlayerWindow : Window
    {
        // Normalde LiveTvPage'den gönderilen VideoView
        private readonly VideoView? _videoView;

        /// <summary>
        /// XAML / Designer için parametresiz ctor.
        /// Runtime'da da çağrılabilir ama _videoView null kalır.
        /// </summary>
        public FullScreenPlayerWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// LiveTvPage'den tam ekran için kullanılan ctor.
        /// </summary>
        public FullScreenPlayerWindow(VideoView videoView) : this()
        {
            _videoView = videoView ?? throw new ArgumentNullException(nameof(videoView));

            try
            {
                // VideoView'ı tam ekran penceresinin root'ına taşı
                VideoHostRoot.Children.Add(_videoView);
            }
            catch
            {
                // Görsel ağaç hata verirse UI çökmesin
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Burada sadece tam ekran içindeki referansı temizliyoruz;
                // VideoHost tekrar LiveTvPage'de yerine takılıyor.
                if (_videoView is not null && VideoHostRoot.Children.Contains(_videoView))
                {
                    VideoHostRoot.Children.Remove(_videoView);
                }
            }
            catch
            {
            }

            base.OnClosed(e);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void FullScreenFitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🔐 İlk guard: ctor henüz VideoView vermemişse (InitializeComponent sırasında) hemen çık
            if (_videoView is null)
                return;

            var player = _videoView.MediaPlayer;
            if (player is null)
                return;

            try
            {
                var combo = (ComboBox)sender;
                var item = combo.SelectedItem as ComboBoxItem;
                var tag = item?.Tag as string ?? "Fit";

                switch (tag)
                {
                    case "Fill":
                        player.AspectRatio = "16:9";
                        player.Scale = 0;
                        break;

                    case "Original":
                        player.AspectRatio = null;
                        player.Scale = 1;
                        break;

                    default: // Fit
                        player.AspectRatio = null;
                        player.Scale = 0;
                        break;
                }
            }
            catch
            {
                // Sessiz geç – görüntüyü bozmak istemiyoruz
            }
        }
    }
}