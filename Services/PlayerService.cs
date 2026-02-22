using System;
using System.Windows;
using LibVLCSharp.Shared;

namespace AtlasHub.Services;

public sealed class PlayerService : IDisposable
{
    private LibVLC? _libVlc;

    public MediaPlayer? Player { get; private set; }

    public PlayerService()
    {
        // Native lib yükleme / init
        Core.Initialize();

        _libVlc = new LibVLC(
            "--no-video-title-show",
            "--network-caching=1500",
            "--file-caching=1500",
            "--live-caching=1500",
            "--codec=avcodec"
        );

        Player = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = true
        };
    }

    public void Play(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (_libVlc is null || Player is null) return;

        Stop();

        using var media = new Media(_libVlc, new Uri(url.Trim()));
        Player.Play(media);
    }

    public void Pause()
    {
        if (Player?.CanPause == true) Player.Pause();
    }

    public void Stop()
    {
        try
        {
            if (Player?.IsPlaying == true) Player.Stop();
        }
        catch { }
    }

    public void SetMute(bool mute)
    {
        try
        {
            if (Player is not null) Player.Mute = mute;
        }
        catch { }
    }

    public void SetVolume(int volume0to100)
    {
        try
        {
            if (Player is null) return;
            var v = Math.Clamp(volume0to100, 0, 100);
            Player.Volume = v;
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            // LibVLC dispose’u UI thread’de daha stabil (WPF VideoView + native callbacks)
            var disp = Application.Current?.Dispatcher;
            if (disp is not null && !disp.CheckAccess())
            {
                disp.Invoke(DisposeCore);
                return;
            }

            DisposeCore();
        }
        catch
        {
            // AccessViolation dahil: kapanışı bozmayalım
        }
    }

    private void DisposeCore()
    {
        try
        {
            if (Player is not null)
            {
                // 1) playback’i durdur
                try { Player.Stop(); } catch { }

                // 2) Media referansını kopar (native callback riskini azaltır)
                try { Player.Media?.Dispose(); } catch { }
                try { Player.Media = null; } catch { }

                // 3) Player dispose
                try { Player.Dispose(); } catch { }
                Player = null;
            }

            // 4) LibVLC dispose
            try { _libVlc?.Dispose(); } catch { }
            _libVlc = null;
        }
        catch
        {
            // native layer bazen yine de patlayabiliyor; kapanışı bozmayalım
        }
    }
}
