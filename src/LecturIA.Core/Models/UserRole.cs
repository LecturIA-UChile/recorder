namespace LecturIA.Core.Models;

/// <summary>
/// Roles assigned by the LecturIA identity provider.
/// </summary>
public enum UserRole
{
    /// <summary>Teacher responsible for recording student readings.</summary>
    Professor,

    /// <summary>School leader with institution-level visibility.</summary>
    Director,

    /// <summary>Operator responsible for maintaining portal data.</summary>
    Maintainer,

    /// <summary>Platform administrator.</summary>
    Administrator,
}
