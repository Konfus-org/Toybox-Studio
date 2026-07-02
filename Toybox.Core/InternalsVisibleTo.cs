using System.Runtime.CompilerServices;

// The engine-sync framework's base types (EngineSyncedObject, EngineSyncValue) live here, but their
// internal contract (SetWireAsync / HydrateFromDescribe / WriteSyncedInto, the reflection helpers) is
// consumed by the synced domain types and the property grid in the projects above. Splitting them into
// this assembly would otherwise hide those internals; grant access rather than widening them to public.
// Add each consuming assembly as it is extracted from Toybox.Studio.
[assembly: InternalsVisibleTo("Toybox.Studio")]
