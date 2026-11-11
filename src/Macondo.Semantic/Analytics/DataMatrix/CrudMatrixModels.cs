using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataMatrix;

/// <summary>
/// Flags of the types of CRUD operations performed on a data entity or table.
/// </summary>
[Flags]
public enum CrudOperation
{
    None = 0,
    Create = 1 << 0,
    Read = 1 << 1,
    Update = 1 << 2,
    Delete = 1 << 3,
    All = Create | Read | Update | Delete
}

/// <summary>
/// Single CRUD interaction cell between a data entity (table/model) and a routine/module.
/// </summary>
public sealed class CrudCell
{
    public string EntityName { get; init; } = string.Empty;
    public Symbol RoutineSymbol { get; init; } = null!;
    public Symbol? ContainerSymbol { get; init; }
    public CrudOperation Operations { get; set; } = CrudOperation.None;

    public bool HasCreate => (Operations & CrudOperation.Create) != 0;
    public bool HasRead => (Operations & CrudOperation.Read) != 0;
    public bool HasUpdate => (Operations & CrudOperation.Update) != 0;
    public bool HasDelete => (Operations & CrudOperation.Delete) != 0;

    public string CrudString
    {
        get
        {
            var chars = new char[4];
            chars[0] = HasCreate ? 'C' : '-';
            chars[1] = HasRead ? 'R' : '-';
            chars[2] = HasUpdate ? 'U' : '-';
            chars[3] = HasDelete ? 'D' : '-';
            return new string(chars);
        }
    }
}

/// <summary>
/// Summary of all CRUD operations relating to a specific database entity/table.
/// </summary>
public sealed class EntityCrudSummary
{
    public string EntityName { get; init; } = string.Empty;
    public List<CrudCell> Accessors { get; init; } = new();

    public int TotalAccessorsCount => Accessors.Count;
    public int CreateCount => Accessors.Count(a => a.HasCreate);
    public int ReadCount => Accessors.Count(a => a.HasRead);
    public int UpdateCount => Accessors.Count(a => a.HasUpdate);
    public int DeleteCount => Accessors.Count(a => a.HasDelete);
}

/// <summary>
/// Complete bidirectional CRUD matrix of the project.
/// </summary>
public sealed class CrudMatrixDataset
{
    public List<string> Entities { get; set; } = new();
    public List<EntityCrudSummary> EntitySummaries { get; set; } = new();
    public List<CrudCell> AllCells { get; set; } = new();

    public int TotalEntities => Entities.Count;
    public int TotalCrudOperations => AllCells.Count;
}
