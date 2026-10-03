using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

// Week 3 사전 검증(규칙 D-16): 설치 파일 없이 실행하는(unpackaged) exe에서 Windows.Media.Ocr이 동작하는가?
// 사용자 화면은 쓰지 않고, 정답을 아는 합성 이미지로만 측정한다.

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
var log = new StringBuilder();
void Say(string s) { Console.WriteLine(s); log.AppendLine(s); }

Say($"OS: {Environment.OSVersion}");
Say($"패키지 신원 있음(MSIX)? {Native.HasPackageIdentity()}");

IReadOnlyList<Windows.Globalization.Language> langs;
try
{
    langs = OcrEngine.AvailableRecognizerLanguages;
}
catch (Exception ex)
{
    Say($"AvailableRecognizerLanguages 호출 실패: {ex.GetType().Name}: {ex.Message}");
    File.WriteAllText(Path.Combine(outDir, "ocr_results.txt"), log.ToString(), Encoding.UTF8);
    return 1;
}
Say("설치된 OCR 언어: " + (langs.Count == 0 ? "(없음)" : string.Join(", ", langs.Select(l => $"{l.LanguageTag}({l.DisplayName})"))));

var engines = new List<(string Name, OcrEngine Engine)>();
foreach (var tag in new[] { "ko", "en-US" })
{
    var lang = langs.FirstOrDefault(l => l.LanguageTag.StartsWith(tag, StringComparison.OrdinalIgnoreCase));
    if (lang == null) { Say($"- {tag}: OCR 언어 없음"); continue; }
    var engine = OcrEngine.TryCreateFromLanguage(lang);
    Say($"- {tag}: 엔진 생성 {(engine == null ? "실패" : "성공")}");
    if (engine != null) engines.Add((lang.LanguageTag, engine));
}
var profile = OcrEngine.TryCreateFromUserProfileLanguages();
Say($"- 사용자 프로필 언어로 엔진 생성: {(profile == null ? "실패" : "성공 (" + profile.RecognizerLanguage.LanguageTag + ")")}");
if (profile != null) engines.Add(("프로필:" + profile.RecognizerLanguage.LanguageTag, profile));
Say($"최대 이미지 크기: {OcrEngine.MaxImageDimension}px");
if (engines.Count == 0) { Say("사용 가능한 엔진이 없어 중단"); File.WriteAllText(Path.Combine(outDir, "ocr_results.txt"), log.ToString(), Encoding.UTF8); return 2; }

// ---------- 합성 이미지 생성 ----------
var samples = new (string Id, string Text, string Font)[]
{
    ("ko-ui", "로그인에 실패했습니다. 아이디 또는 비밀번호를 확인해 주세요.", "Malgun Gothic"),
    ("en-err", "Error 500: Internal Server Error at /api/login", "Segoe UI"),
    ("mixed", "결제 화면에서 오류가 발생했습니다 (코드 E-4021) TypeError: Cannot read properties of undefined", "Malgun Gothic"),
    ("code", "for (int i = 0; i < items.Count; i++) { total += items[i].Price; }", "Consolas"),
};
var sizes = new[] { 11, 13, 16, 22 };
var themes = new[] { ("light", Color.Black, Color.White), ("dark", Color.FromArgb(212, 212, 212), Color.FromArgb(30, 30, 30)) };

var cases = new List<(string Id, string Truth, string Path)>();
foreach (var s in samples)
foreach (var size in sizes)
foreach (var (themeName, fg, bg) in themes)
{
    string path = Path.Combine(outDir, $"{s.Id}_{size}px_{themeName}.png");
    using var font = new Font(s.Font, size, FontStyle.Regular, GraphicsUnit.Pixel);
    using var probe = new Bitmap(1, 1);
    using var pg = Graphics.FromImage(probe);
    var measured = pg.MeasureString(s.Text, font);
    int w = (int)Math.Ceiling(measured.Width) + 40, h = (int)Math.Ceiling(measured.Height) + 30;
    using var bmp = new Bitmap(w, h);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(bg);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var brush = new SolidBrush(fg);
        g.DrawString(s.Text, font, brush, 20, 15);
    }
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    cases.Add(($"{s.Id} {size}px {themeName}", s.Text, path));
}

// ---------- 인식 + 정확도 ----------
static string Norm(string t) => new string(t.Where(c => !char.IsWhiteSpace(c)).ToArray());
static double Similarity(string a, string b)
{
    a = Norm(a); b = Norm(b);
    if (a.Length == 0 && b.Length == 0) return 1;
    int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
    for (int j = 0; j <= b.Length; j++) prev[j] = j;
    for (int i = 1; i <= a.Length; i++)
    {
        cur[0] = i;
        for (int j = 1; j <= b.Length; j++)
            cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        (prev, cur) = (cur, prev);
    }
    return 1.0 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
}

static async Task<(string Text, long Ms)> RecognizeAsync(OcrEngine engine, string path)
{
    using var stream = File.OpenRead(path).AsRandomAccessStream();
    var decoder = await BitmapDecoder.CreateAsync(stream);
    using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    var sw = Stopwatch.StartNew();
    var result = await engine.RecognizeAsync(bitmap);
    sw.Stop();
    return (result.Text, sw.ElapsedMilliseconds);
}

static async Task<(List<string> Lines, long Ms)> RecognizeLinesAsync(OcrEngine engine, string path)
{
    using var stream = File.OpenRead(path).AsRandomAccessStream();
    var decoder = await BitmapDecoder.CreateAsync(stream);
    using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    var sw = Stopwatch.StartNew();
    var result = await engine.RecognizeAsync(bitmap);
    sw.Stop();
    return (result.Lines.Select(l => l.Text).ToList(), sw.ElapsedMilliseconds);
}

static string Upscale(string path, int k)
{
    string outPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"_x{k}.png");
    using var src = new Bitmap(path);
    using var dst = new Bitmap(src.Width * k, src.Height * k);
    using (var g = Graphics.FromImage(dst))
    {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
    }
    dst.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
    return outPath;
}

Say("");
Say("=== 정확도(공백 제외 문자 단위 유사도, 1.00 = 완벽) ===");
var header = "케이스".PadRight(26) + string.Join("", engines.Select(e => e.Name.PadRight(12)));
Say(header);
var totals = engines.ToDictionary(e => e.Name, _ => new List<double>());
var details = new StringBuilder();
foreach (var c in cases)
{
    var row = new StringBuilder(c.Id.PadRight(26));
    foreach (var (name, engine) in engines)
    {
        var (text, ms) = await RecognizeAsync(engine, c.Path);
        double sim = Similarity(c.Truth, text);
        totals[name].Add(sim);
        row.Append(sim.ToString("0.00").PadRight(12));
        if (sim < 0.9) details.AppendLine($"  [{c.Id}] {name} 정답: {c.Truth}\n  [{c.Id}] {name} 인식: {text.Replace("\r", " ").Replace("\n", " ")}");
    }
    Say(row.ToString());
}
Say("");
foreach (var (name, list) in totals) Say($"평균 {name}: {list.Average():0.000}  (최저 {list.Min():0.00})");
Say("");
Say("=== 0.90 미만 사례 상세 ===");
Say(details.Length == 0 ? "(없음)" : details.ToString());

// ---------- 작은 글자 확대 효과 ----------
Say("=== 작은 글자(11px, 13px)를 확대한 뒤 인식했을 때 유사도 (ko 엔진) ===");
Say("케이스".PadRight(26) + "x1".PadRight(8) + "x2".PadRight(8) + "x3".PadRight(8));
var scaleTotals = new Dictionary<int, List<double>> { [1] = new(), [2] = new(), [3] = new() };
foreach (var c in cases.Where(c => c.Id.Contains(" 11px") || c.Id.Contains(" 13px")))
{
    var row = new StringBuilder(c.Id.PadRight(26));
    foreach (var k in new[] { 1, 2, 3 })
    {
        var p = k == 1 ? c.Path : Upscale(c.Path, k);
        var (t, _) = await RecognizeAsync(engines[0].Engine, p);
        double sim = Similarity(c.Truth, t);
        scaleTotals[k].Add(sim);
        row.Append(sim.ToString("0.00").PadRight(8));
    }
    Say(row.ToString());
}
Say($"평균  x1: {scaleTotals[1].Average():0.000}   x2: {scaleTotals[2].Average():0.000}   x3: {scaleTotals[3].Average():0.000}");
Say("");

// ---------- 화면 캡처 같은 큰 이미지: 속도 ----------
string bigPath = Path.Combine(outDir, "screen_like_1920x1080.png");
using (var big = new Bitmap(1920, 1080))
using (var g = Graphics.FromImage(big))
{
    g.Clear(Color.White);
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    using var title = new Font("Malgun Gothic", 22, FontStyle.Bold, GraphicsUnit.Pixel);
    using var body = new Font("Malgun Gothic", 15, FontStyle.Regular, GraphicsUnit.Pixel);
    using var mono = new Font("Consolas", 15, FontStyle.Regular, GraphicsUnit.Pixel);
    g.DrawString("오류가 발생했습니다", title, Brushes.Black, 80, 60);
    for (int i = 0; i < 22; i++)
        g.DrawString($"{i + 1}. 결제 처리 중 서버에서 응답하지 않습니다. 잠시 후 다시 시도해 주세요. (요청 번호 REQ-{1000 + i})", body, Brushes.Black, 80, 120 + i * 28);
    for (int i = 0; i < 14; i++)
        g.DrawString($"at com.example.pay.PaymentService.process(PaymentService.java:{100 + i}) caused by NullPointerException", mono, Brushes.DarkRed, 80, 780 + i * 20);
    big.Save(bigPath, System.Drawing.Imaging.ImageFormat.Png);
}
Say("=== 1920x1080 화면 크기 이미지(텍스트 약 36줄) ===");
foreach (var (name, engine) in engines.Take(1))
{
    var (lines, ms) = await RecognizeLinesAsync(engine, bigPath);
    Say($"{name}: {ms}ms, Lines 기준 {lines.Count}줄 인식 (정답 37줄: 제목 1 + 본문 22 + 로그 14)");
    foreach (var (l, i) in lines.Select((l, i) => (l, i)).Take(6)) Say($"  줄{i + 1}: {l}");
    Say("  ...");
    foreach (var (l, i) in lines.Select((l, i) => (l, i)).Skip(Math.Max(0, lines.Count - 3))) Say($"  줄{i + 1}: {l}");
}
{
    var (name, engine) = engines[0];
    var (lines, _) = await RecognizeLinesAsync(engine, bigPath);
    int hit = lines.Count(l => l.Contains("서버에서") || l.Contains("REQ-"));
    Say($"본문 22줄 중 '서버에서' 또는 'REQ-'가 인식된 줄: {hit}");
}
var bigX2 = Upscale(bigPath, 2);
{
    var (name, engine) = engines[0];
    var (lines, ms) = await RecognizeLinesAsync(engine, bigX2);
    Say($"x2 확대(3840x2160) 후: {ms}ms, {lines.Count}줄, 첫 줄: {lines.FirstOrDefault()}, 둘째 줄: {lines.Skip(1).FirstOrDefault()}");
}

File.WriteAllText(Path.Combine(outDir, "ocr_results.txt"), log.ToString(), Encoding.UTF8);
return 0;

static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    public static bool HasPackageIdentity()
    {
        int len = 0;
        return GetCurrentPackageFullName(ref len, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
    }
}
