using PhaseDiagram.Components;
using PhaseDiagram.Services.Thermo;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<CoolPropPhaseEnvelopeService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
    }
});

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

_ = Task.Run(() =>
{
    try
    {
        var sw = Stopwatch.StartNew();
        var (pressure, ok) = CoolPropSharpWrapper.GetSaturationPressureAtTQ(
            "HEOS", ["Methane"], [1.0], 150.0, 0.0);
        sw.Stop();

        string msg = ok
            ? $"✅ CoolProp OK — P = {pressure:F0} Pa (init: {sw.ElapsedMilliseconds} ms)"
            : $"❌ CoolProp returned failure after {sw.ElapsedMilliseconds} ms";

        Console.WriteLine(msg);
        Debug.WriteLine($"[CoolProp Test] {msg}");
    }
    catch (DllNotFoundException ex)
    {
        Console.WriteLine($"❌ CoolProp DLL not found: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ CoolProp failed: {ex.GetType().Name}: {ex.Message}");
    }
});

app.Run();