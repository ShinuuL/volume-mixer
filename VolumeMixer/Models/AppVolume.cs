namespace VolumeMixer.Models;

/// <summary>Estado do volume master do dispositivo padrão.</summary>
public sealed record MasterInfo(double VolumePercent, bool Mute);

/// <summary>Linha do mixer: todas as sessões de áudio de um processo, agrupadas por PID.</summary>
public sealed record AppVolume(
    int ProcessId,
    string ProcessName,
    byte[]? IconPng,
    double VolumePercent,
    bool Mute);
