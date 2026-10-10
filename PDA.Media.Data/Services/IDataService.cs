namespace PDA.Media.Data.Services;

/// <summary>
/// Basic record operations shared by the data services. Every method uses its own short-lived DataContext, so the
/// records returned are detached: change them and pass them to UpdateRecord to save.
/// </summary>
public interface IDataService<T> where T : class
{
    T? GetRecordById(int id);
    List<T> GetAllRecords();
    List<T> GetAllRecords(string searchTerm);
    T AddRecord(T record);
    T? UpdateRecord(int id, T record);
    bool DeleteRecord(int id);
    bool DeleteRecord(T record);

    Task<T?> GetRecordByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<T>> GetAllRecordsAsync(CancellationToken cancellationToken = default);
    Task<List<T>> GetAllRecordsAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task<T> AddRecordAsync(T record, CancellationToken cancellationToken = default);
    Task<T?> UpdateRecordAsync(int id, T record, CancellationToken cancellationToken = default);
    Task<bool> DeleteRecordAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteRecordAsync(T record, CancellationToken cancellationToken = default);
}
