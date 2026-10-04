using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dispatch.Kit.Models;

namespace Dispatch.Kit.Services;

public class HandlerService(HttpClient http)
{
    public async Task<Guid> CreateAsync(
        string? slug,
        string title,
        string description,
        Stream file,
        string filename,
        CancellationToken cancellationToken = default
    )
    {
        using var form = new MultipartFormDataContent();

        var module = new StreamContent(file);
        module.Headers.ContentType = new MediaTypeHeaderValue("application/wasm");

        form.Add(module, "file", filename);
        form.Add(new StringContent(title.Trim()), "title");
        form.Add(new StringContent(description.Trim()), "description");
        if (slug?.Trim() is [_, ..] text)
        {
            form.Add(new StringContent(text), "slug");    
        }

        using var response = await http.PostAsync("/handlers", form, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Conflict:
                throw new InvalidOperationException("A handler with this slug already exists.");

            case HttpStatusCode.BadRequest:
                var isJson = response.Content.Headers.ContentType?.MediaType?.EndsWith("json") is true;
                var problem = isJson
                    ? await response.Content.ReadFromJsonAsync<ValidationProblem>(cancellationToken)
                    : null;

                throw new InvalidOperationException(
                    problem is { Errors.Count: > 0 }
                        ? string.Join(" ", problem.Errors.Values.SelectMany(messages => messages))
                        : "The request was rejected."
                );
        }

        response.EnsureSuccessStatusCode();
        return Guid.Parse(response.Headers.Location!.Segments[^1].TrimEnd('/'));
    }

    public async Task<List<HandlerData>> ListAsync(CancellationToken cancellationToken = default)
    {
        var handlers = await http.GetFromJsonAsync<List<HandlerData>>("/handlers", cancellationToken);
        return handlers!;
    }

    public async Task<HandlerData?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<HandlerData>($"/handlers/{id}", cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await http.DeleteAsync($"/handlers/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    
    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);
}