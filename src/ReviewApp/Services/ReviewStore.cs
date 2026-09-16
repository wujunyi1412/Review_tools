using System.Text.Json;
using ImageReviewTool.Models;

namespace ImageReviewTool.Services;

public sealed class ReviewStore
{
    private const string FileName = ".review-data.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<ReviewDatabase> LoadAsync(string root)
    {
        var path = Path.Combine(root, FileName);
        if (!File.Exists(path)) return new ReviewDatabase();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReviewDatabase>(stream, Options) ?? new ReviewDatabase();
    }

    public async Task SaveAsync(string root, ReviewDatabase database)
    {
        var destination = Path.Combine(root, FileName);
        var temporary = destination + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, database, Options);
        File.Move(temporary, destination, true);
    }
}
