using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClipBoardX.Models;

namespace ClipBoardX.Services;

/// <summary>
/// Читает содержимое системного буфера обмена в безопасном виде.
/// </summary>
internal static class ClipboardReader
{
    /// <summary>Флаг, чтобы не записывать собственные изменения, которые делает ClipBoardX.</summary>
    [ThreadStatic]
    private static bool _internalWrite;

    private const int MaxClipboardRetries = 8;

    public static void SetInternalWriteFlag(bool value) => _internalWrite = value;

    public static bool IsInternalWrite => _internalWrite;

    public static ClipItem? Capture(AppSettings settings)
    {
        for (var attempt = 0; attempt < MaxClipboardRetries; attempt++)
        {
            try
            {
                return CaptureCore(settings);
            }
            catch (ExternalException)
            {
                // Буфер занят другим приложением — короткая пауза и повтор.
                Thread.Sleep(25 + attempt * 15);
            }
            catch (ThreadStateException)
            {
                return null;
            }
        }

        return null;
    }

    private static ClipItem? CaptureCore(AppSettings settings)
    {
        if (_internalWrite) return null;
        if (!Clipboard.ContainsText() && !Clipboard.ContainsFileDropList()
            && !(settings.CaptureImages && Clipboard.ContainsImage())
            && !(settings.CaptureHtml && Clipboard.ContainsData(DataFormats.Html)))
        {
            return null;
        }

        var item = TryCaptureText();
        if (item is null && settings.CaptureHtml) item = TryCaptureHtml();
        if (item is null && settings.CaptureFiles) item = TryCaptureFiles();
        if (item is null && settings.CaptureImages) item = TryCaptureImage();

        if (item is null) return null;

        if (item.Kind == ClipKind.Text)
        {
            var len = item.Text.Length;
            if (len == 0) return null;
            if (settings.MinTextLength > 0 && len < settings.MinTextLength) return null;
        }

        // Для изображений хэш уже посчитан по пикселям — не пересчитываем по файлу.
        if (item.Hash.Length == 0)
        {
            item.Hash = ComputeHash(item);
        }

        return item;
    }

    private static ClipItem? TryCaptureText()
    {
        if (!Clipboard.ContainsText()) return null;

        string? text;
        try
        {
            text = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch (Exception)
        {
            text = null;
        }

        if (string.IsNullOrEmpty(text)) return null;

        return new ClipItem
        {
            Kind = ClipKind.Text,
            Text = text
        };
    }

    private static ClipItem? TryCaptureHtml()
    {
        try
        {
            if (!Clipboard.ContainsData(DataFormats.Html)) return null;

#pragma warning disable WFDEV005 // GetData(string) нужен для нестандартного формата HTML Format.
            var data = Clipboard.GetData(DataFormats.Html) as MemoryStream;
#pragma warning restore WFDEV005
            if (data is null) return null;

            var html = Encoding.UTF8.GetString(data.ToArray());
            if (string.IsNullOrWhiteSpace(html)) return null;

            return new ClipItem
            {
                Kind = ClipKind.Html,
                Text = StripHtml(html)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ClipItem? TryCaptureFiles()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return null;

            var files = Clipboard.GetFileDropList();
            if (files is null || files.Count == 0) return null;

            var list = new List<string>(files.Count);
            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file)) list.Add(file);
            }

            if (list.Count == 0) return null;

            return new ClipItem
            {
                Kind = ClipKind.Files,
                Files = list
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ClipItem? TryCaptureImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return null;

            using var raw = Clipboard.GetImage();
            if (raw is null) return null;

            if (raw.Width < 2 && raw.Height < 2) return null;

            using var image = raw is Bitmap bmp ? bmp : new Bitmap(raw);

            // Считаем хэш пикселей, чтобы одинаковые картинки не плодили файлы на диске.
            var hash = HashBitmap(image);
            var dir = AppPaths.ImagesDir;
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, hash.ToLowerInvariant() + ".png");
            if (!File.Exists(path))
            {
                image.Save(path, ImageFormat.Png);
            }

            return new ClipItem
            {
                Kind = ClipKind.Image,
                ImagePath = path,
                Hash = hash
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Хэш содержимого Bitmap через пиксельные данные.</summary>
    private static string HashBitmap(Bitmap bitmap)
    {
        using var clone = new Bitmap(bitmap);

        var rect = new Rectangle(0, 0, clone.Width, clone.Height);
        var data = clone.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var length = Math.Abs(data.Stride) * clone.Height;
            var buffer = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, length);
            return Convert.ToHexString(SHA256.HashData(buffer));
        }
        finally
        {
            clone.UnlockBits(data);
        }
    }

    public static string StripHtml(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"&\w+;", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    public static string ComputeHash(ClipItem item)
    {
        var sb = new StringBuilder();
        sb.Append((int)item.Kind).Append('|');

        switch (item.Kind)
        {
            case ClipKind.Text:
            case ClipKind.Html:
                sb.Append(item.Text);
                break;
            case ClipKind.Files:
                sb.Append(string.Join('\n', item.Files));
                break;
            case ClipKind.Image:
                sb.Append(HashFile(item.ImagePath));
                break;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string HashFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}