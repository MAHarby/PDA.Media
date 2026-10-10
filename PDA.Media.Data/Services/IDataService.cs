namespace PDA.Media.Data.Services;

public interface IDataService<T> where T : class
{
    T? GetRecordById(int id);
    List<T> GetAllRecords();
    List<T> GetAllRecords(string searchTerm);
    T? AddRecord(T? record);
    T? UpdateRecord(int id, T? record);
    bool DeleteRecord(int id);
    bool DeleteRecord(T? record);

    Task<T?> GetRecordByIdAsync(int id);
    Task<List<T>> GetAllRecordsAsync();
    Task<List<T>> GetAllRecordsAsync(string searchTerm);
    Task<T?> AddRecordAsync(T? record);
    Task<T?> UpdateRecordAsync(int id, T? record);
    Task<bool> DeleteRecordAsync(int id);
    Task<bool> DeleteRecordAsync(T? record);
}