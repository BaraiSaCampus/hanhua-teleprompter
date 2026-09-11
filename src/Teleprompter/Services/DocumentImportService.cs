using System.Security.Cryptography;
using System.Text;
using System.IO;
using DocSharp.Docx;
using Teleprompter.Models;

namespace Teleprompter.Services;

public sealed record ImportResult(string SourcePath, SourceFingerprint Fingerprint, IReadOnlyList<string> Lines);

public sealed class DocumentImportService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<ImportResult> ImportAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Import(path, cancellationToken), cancellationToken);

    public ImportResult Import(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到所选文件。", fullPath);

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".txt" or ".docx" or ".doc"))
            throw new NotSupportedException("只支持 TXT、DOCX 和 DOC 文件。");

        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = CreateFingerprint(fullPath);
        var text = extension switch
        {
            ".txt" => ReadText(fullPath),
            ".docx" => ReadDocx(fullPath),
            ".doc" => ReadLegacyDoc(fullPath),
            _ => throw new NotSupportedException()
        };

        cancellationToken.ThrowIfCancellationRequested();
        var lines = SplitLines(text);
        if (lines.Count == 0) throw new InvalidDataException("文件中没有可用的非空文本行。");
        return new ImportResult(fullPath, fingerprint, lines);
    }

    public static IReadOnlyList<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    public static SourceFingerprint CreateFingerprint(string path)
    {
        var info = new FileInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        info.Refresh();
        return new SourceFingerprint
        {
            Length = info.Length,
            LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
            Sha256 = hash
        };
    }

    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            var gb18030 = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return gb18030.GetString(bytes);
        }
    }

    private static string ReadText(string path) => DecodeText(File.ReadAllBytes(path));

    private static string ReadDocx(string path)
    {
        var tempText = Path.Combine(Path.GetTempPath(), $"teleprompter-{Guid.NewGuid():N}.txt");
        try
        {
            var converter = new DocxToTxtConverter { OriginalFolderPath = Path.GetDirectoryName(path) };
            converter.Convert(path, tempText);
            return ReadText(tempText);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("无法读取 DOCX；文件可能已损坏、加密或格式不受支持。", ex);
        }
        finally
        {
            TryDelete(tempText);
        }
    }

    private static string ReadLegacyDoc(string path)
    {
        var tempDocx = Path.Combine(Path.GetTempPath(), $"teleprompter-{Guid.NewGuid():N}.docx");
        try
        {
            using (var reader = new DocSharp.Binary.StructuredStorage.Reader.StructuredStorageReader(path))
            {
                var document = new DocSharp.Binary.DocFileFormat.WordDocument(reader);
                using var converted = DocSharp.Binary.OpenXmlLib.WordprocessingML.WordprocessingDocument.Create(
                    tempDocx,
                    DocSharp.Binary.OpenXmlLib.WordprocessingDocumentType.Document);
                DocSharp.Binary.WordprocessingMLMapping.Converter.Convert(document, converted);
            }

            return ReadDocx(tempDocx);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("无法读取 DOC；文件可能已损坏、加密或不是 Word 97-2003 格式。", ex);
        }
        finally
        {
            TryDelete(tempDocx);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
