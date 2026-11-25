namespace DXSharp.Houdini.Interop;

public readonly record struct HapiVersion(
    int HoudiniMajor, int HoudiniMinor, int HoudiniBuild, int HoudiniPatch,
    int EngineMajor, int EngineMinor, int ApiRevision ) {
    public override string ToString() =>
        $"Houdini {HoudiniMajor}.{HoudiniMinor}.{HoudiniBuild}.{HoudiniPatch}; Engine {EngineMajor}.{EngineMinor}; API {ApiRevision}";
}
