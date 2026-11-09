namespace InspectionReview.Web.Storage;

/// <summary>
/// File storage abstraction. The production implementation will use Azure Blob Storage;
/// services depend only on this interface.
/// </summary>
public interface IFileStorage
{
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
