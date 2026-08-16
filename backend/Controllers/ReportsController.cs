using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentManagement.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")]
public class ReportsController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    public ReportsController(IConfiguration cfg, IWebHostEnvironment env)
    {
        _cfg = cfg;
        _env = env;
    }

    /// <summary>
    /// Kicks the Python reporting job (Phase 7) and returns the file paths.
    ///
    /// Pattern: "Batch + file pickup" (per project decision). The Python script
    /// is invoked as a subprocess, writes output to a known "reports" folder,
    /// and the .NET API returns the file URLs. Tradeoff chosen: this is simpler
    /// than running a separate FastAPI microservice and is fine because the
    /// "Generate report" UX is "wait ~5s, then download" rather than a long
    /// live progress spinner. If the job ever takes >30s or we want a real
    /// progress UI, switch to a queue + worker pattern.
    ///
    /// We expose two outputs: the latest generated files, and a refresh action.
    /// </summary>
    public record ReportArtifact(string Name, string Url, long SizeBytes, DateTime GeneratedAt);
    public record ReportRefreshResult(string ExcelUrl, string PptUrl, string Stdout, int ExitCode);

    [HttpGet("latest")]
    public IActionResult Latest()
    {
        var dir = ReportsPath();
        if (!Directory.Exists(dir)) return Ok(Array.Empty<ReportArtifact>());
        var files = Directory.EnumerateFiles(dir)
            .Select(f => new ReportArtifact(
                Name: Path.GetFileName(f),
                Url: $"/api/reports/download/{Uri.EscapeDataString(Path.GetFileName(f))}",
                SizeBytes: new FileInfo(f).Length,
                GeneratedAt: new FileInfo(f).LastWriteTimeUtc))
            .OrderByDescending(a => a.GeneratedAt)
            .ToList();
        return Ok(files);
    }

    [HttpGet("download/{name}")]
    public IActionResult Download(string name)
    {
        var path = Path.Combine(ReportsPath(), name);
        if (!System.IO.File.Exists(path)) return NotFound();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var mime = ext switch
        {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream",
        };
        return PhysicalFile(path, mime, name);
    }

    /// <summary>Triggers the Python job. Blocks until the script finishes (or times out
    /// after 60s) and returns the generated file URLs. Long-term: move to a queue
    /// (Hangfire / Azure Functions) so the admin gets a "generating…" UI.</summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(CancellationToken ct)
    {
        var scriptPath = Path.Combine(_env.ContentRootPath, "..", "reporting", "generate_reports.py");
        scriptPath = Path.GetFullPath(scriptPath);
        if (!System.IO.File.Exists(scriptPath))
            return NotFound(new { error = $"Reporting script not found at {scriptPath}" });

        var psi = new System.Diagnostics.ProcessStartInfo("python", $"\"{scriptPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath),
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            return StatusCode(504, new { error = "Reporting job timed out after 60s." });
        }
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        var exit = p.ExitCode;
        if (exit != 0)
            return StatusCode(500, new { error = "Reporting job failed.", exitCode = exit, stderr, stdout });

        // Find the newest xlsx + pptx written
        var dir = ReportsPath();
        var xlsx = Directory.EnumerateFiles(dir, "*.xlsx").OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).FirstOrDefault();
        var pptx = Directory.EnumerateFiles(dir, "*.pptx").OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc).FirstOrDefault();
        return Ok(new ReportRefreshResult(
            ExcelUrl: xlsx is null ? "" : $"/api/reports/download/{Uri.EscapeDataString(Path.GetFileName(xlsx))}",
            PptUrl:   pptx is null ? "" : $"/api/reports/download/{Uri.EscapeDataString(Path.GetFileName(pptx))}",
            Stdout: stdout,
            ExitCode: exit));
    }

    private string ReportsPath()
    {
        var p = _cfg["Reports:OutputDir"]
            ?? Path.Combine(_env.ContentRootPath, "..", "reporting", "output");
        return Path.GetFullPath(p);
    }
}
