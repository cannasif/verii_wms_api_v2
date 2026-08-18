using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;

namespace verii_wms_api_v2.QueryTests;

internal sealed class TestNetsisImportOpenFileReader(
    IReadOnlyList<NetsisImportOpenFileDto>? files = null) : INetsisImportOpenFileReader
{
    public Task<IReadOnlyList<NetsisImportOpenFileDto>> GetImportOpenFilesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(files ?? (IReadOnlyList<NetsisImportOpenFileDto>)[]);
}
