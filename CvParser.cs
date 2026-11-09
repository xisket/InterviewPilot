using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace InterviewCompanion;

public static class CvParser
{
    public static string Extract(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024)
            throw new InvalidOperationException("Choose a CV smaller than 20 MB.");
        string text;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".pdf":
                using (var pdf = PdfDocument.Open(path))
                {
                    if (pdf.NumberOfPages > 50) throw new InvalidOperationException("Choose a CV with 50 pages or fewer.");
                    var pages = new List<string>();
                    foreach (var page in pdf.GetPages()) pages.Add(ContentOrderTextExtractor.GetText(page));
                    text = string.Join("\n\n", pages);
                }
                break;
            case ".docx":
                using (var zip = ZipFile.OpenRead(path))
                {
                    var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("This file is not a valid Word document.");
                    if (entry.Length > 10 * 1024 * 1024) throw new InvalidDataException("Word document content is too large.");
                    using var stream = entry.Open();
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 10 * 1024 * 1024 });
                    var doc = XDocument.Load(reader);
                    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    text = string.Join("\n", doc.Descendants(w + "p").Select(p => string.Concat(p.Descendants().Where(e => !e.Ancestors(w + "del").Any()).Select(e =>
                        e.Name == w + "t" ? e.Value : e.Name == w + "tab" ? "\t" : e.Name == w + "br" ? "\n" : ""))));
                }
                break;
            default: throw new InvalidOperationException("Choose PDF or Word .docx. Save older .doc files as .docx first.");
        }
        text = text.Replace("\r\n", "\n").Replace('\u00a0', ' ').Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("No readable text found. Scanned PDFs need OCR first; Word CVs must contain selectable text.");
        if (text.Length > 50000) throw new InvalidOperationException("Extracted CV exceeds 50,000 characters. Import a shorter version.");
        return text;
    }

    public static string Analyze(string text, string job)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Import or paste your CV first.");
        var report = new StringBuilder("LOCAL ATS-STYLE REVIEW\n\nThese are text and keyword checks, not a commercial ATS score or a hiring prediction.\n\n");
        var emails = Regex.Matches(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}").Select(m => m.Value).Distinct().ToArray();
        report.AppendLine("Email: " + (emails.Length > 0 ? string.Join(", ", emails) : "Not detected — check your contact details."));
        report.AppendLine("Phone: " + (Regex.IsMatch(text, @"(?<!\w)\+?\d[\d ()-]{7,}\d(?!\w)") ? "Possible phone number detected; verify in the imported text." : "Not detected."));
        report.AppendLine("\nSECTION INDICATORS");
        foreach (var (name, pattern) in new[] {
            ("Experience", @"\b(experience|employment|work history|experiencia)\b"),
            ("Education", @"\b(education|qualifications|educación|formación)\b"),
            ("Skills", @"\b(skills|competencies|technologies|habilidades|competencias)\b") })
            report.AppendLine($"• {name}: {(Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase) ? "term detected" : "not detected; consider a clear heading")}");
        report.AppendLine("\nJOB DESCRIPTION KEYWORDS");
        if (string.IsNullOrWhiteSpace(job)) report.AppendLine("Add a job description and run this check again to compare keywords.");
        else
        {
            var stop = new HashSet<string>("with that this from have will your our you the and for are was were has who can all not but into about their they work team role must such more than years experience working ability skills required responsibilities company looking including strong using based join knowledge excellent opportunity would should also como para con una los las del por que sus este esta experiencia trabajo años".Split(' '));
            var keywords = Regex.Matches(job.ToLowerInvariant(), @"[\p{L}\p{N}][\p{L}\p{N}+#.\-]*")
                .Select(m => m.Value.TrimEnd('.')).Where(s => s.Length >= 3 && !stop.Contains(s) && !s.All(char.IsDigit))
                .GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(35).Select(g => g.Key).ToArray();
            var found = keywords.Where(k => Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(k) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)).ToArray();
            report.AppendLine($"Literal matches: {found.Length} of {keywords.Length} selected terms (no synonym or proficiency inference).");
            report.AppendLine("Present: " + (found.Length == 0 ? "None" : string.Join(", ", found)));
            report.AppendLine("Not found: " + string.Join(", ", keywords.Except(found)));
            report.AppendLine("Add a missing term only when it accurately describes your experience.");
        }
        report.AppendLine("\nREVIEW THE EXTRACTION\nCheck names, dates, bullet points, and reading order in the résumé box, especially for multi-column PDFs. DOCX import reads the main document body, including tables; headers and footers are excluded. No OCR is performed.\n\nYour editable résumé text is used as context for subsequent interview guidance. This check runs locally and sends nothing to an AI service.");
        return report.ToString();
    }
}
