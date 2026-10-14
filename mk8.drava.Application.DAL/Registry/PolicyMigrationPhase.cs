namespace Mk8.Drava.Application.DAL.Registry;

internal enum PolicyMigrationPhase
{
    ArchiveStaged,
    SchemaPrepared,
    SchemaCommitted,
    InitialCheckpointCompleted,
    CleanupSynchronized,
}
