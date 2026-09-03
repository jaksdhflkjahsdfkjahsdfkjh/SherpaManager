namespace SherpaManager.Models;

public sealed class NvidiaSurroundSnapshot
{
    public bool ApiAvailable { get; set; }
    public bool StatusKnown { get; set; }
    public bool HasConfiguredTopology { get; set; }
    public bool Enabled { get; set; }
    public bool IsPossible { get; set; }
    public uint Topology { get; set; }
    public uint PerDisplayWidth { get; set; }
    public uint PerDisplayHeight { get; set; }
    public uint RefreshRateTimes1000 { get; set; }
    public int OverlapX { get; set; }
    public int OverlapY { get; set; }
    public bool GridMembershipVerified { get; set; }
    public uint GridTopologyCount { get; set; }
    public List<NvidiaSurroundGridCellSnapshot> GridCells { get; set; } = [];
    public bool FullGridCaptured { get; set; }
    public List<NvidiaSurroundDisplayGridSnapshot> DisplayGrids { get; set; } = [];
    public string Description { get; set; } = "NVIDIA Surround was not detected.";
}

public sealed class NvidiaSurroundGridCellSnapshot
{
    public uint TopologyIndex { get; set; }
    public uint Row { get; set; }
    public uint Column { get; set; }
    public uint GpuBusId { get; set; }
    public uint DisplayOutputId { get; set; }
    public int OverlapX { get; set; }
    public int OverlapY { get; set; }
}

public sealed class NvidiaSurroundDisplayGridSnapshot
{
    public uint Rows { get; set; }
    public uint Columns { get; set; }
    public bool ApplyWithBezelCorrection { get; set; }
    public bool ImmersiveGaming { get; set; }
    public bool BaseMosaic { get; set; }
    public bool PixelShift { get; set; }
    public uint PerDisplayWidth { get; set; }
    public uint PerDisplayHeight { get; set; }
    public uint BitsPerPixel { get; set; }
    public uint RefreshRate { get; set; }
    public List<NvidiaSurroundDisplayGridCellSnapshot> Displays { get; set; } = [];
}

public sealed class NvidiaSurroundDisplayGridCellSnapshot
{
    public uint Row { get; set; }
    public uint Column { get; set; }
    public uint DisplayId { get; set; }
    public int OverlapX { get; set; }
    public int OverlapY { get; set; }
    public uint Rotation { get; set; }
    public uint CloneGroup { get; set; }
    public uint PixelShiftType { get; set; }
}
