using System.Threading.Tasks;

namespace Test.Library;

public interface IImportedModule
{
    delegate void RecordChanged (Record? record);

    event RecordChanged OnRecordChanged;

    Record? Record { get; set; }

    Task<IImportedInstanced> GetInstanceAsync (string arg);
    Task<int> GetCountAsync ();
    Task<string> GetNameAsync ();
    Task GetVoidAsync ();

    IIsolateHandle GetIsolateHandle ();
    IScopedHandle GetScopedHandle ();
}
