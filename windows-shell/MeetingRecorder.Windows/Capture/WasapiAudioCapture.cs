using System.Runtime.InteropServices;
using System.Text;

namespace MeetingRecorder.Windows.Capture;

internal sealed class WasapiAudioCapture : IDisposable
{
    private readonly AudioDataFlow _dataFlow;
    private readonly string _outputPath;
    private readonly bool _useLoopback;
    private readonly string _label;
    private readonly bool _appendToExisting;
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private Thread? _thread;
    private Exception? _failure;

    public WasapiAudioCapture(AudioDataFlow dataFlow, string outputPath, bool useLoopback, string label, bool appendToExisting = false)
    {
        _dataFlow = dataFlow;
        _outputPath = outputPath;
        _useLoopback = useLoopback;
        _label = label;
        _appendToExisting = appendToExisting;
    }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = $"wasapi-{_label}"
        };
        _thread.Start();
        SpinWait.SpinUntil(() => _thread?.IsAlive == true || _failure is not null, 1000);
        if (_failure is not null)
        {
            throw new InvalidOperationException($"Unable to start {_label} capture.", _failure);
        }
    }

    public void Stop()
    {
        _stopSignal.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        if (_thread?.IsAlive == true)
        {
            throw new TimeoutException($"{_label} capture thread did not stop in time.");
        }

        if (_failure is not null)
        {
            throw new InvalidOperationException($"{_label} capture failed.", _failure);
        }
    }

    public void Dispose()
    {
        try
        {
            Stop();
        }
        catch
        {
        }
        finally
        {
            _stopSignal.Dispose();
        }
    }

    private void CaptureLoop()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr formatPointer = IntPtr.Zero;
        WaveFileWriter? writer = null;

        try
        {
            _ = CoInitializeEx(IntPtr.Zero, 0x0);

            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(_dataFlow, AudioRole.Multimedia, out device));

            var audioClientGuid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref audioClientGuid, 23, IntPtr.Zero, out var audioClientObject));
            audioClient = (IAudioClient)audioClientObject;

            Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out formatPointer));
            var waveFormat = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);
            var formatSize = Marshal.SizeOf<WaveFormatEx>() + waveFormat.cbSize;
            var formatBytes = new byte[formatSize];
            Marshal.Copy(formatPointer, formatBytes, 0, formatSize);

            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath) ?? AppContext.BaseDirectory);
            writer = new WaveFileWriter(_outputPath, formatBytes, _appendToExisting);

            var streamFlags = AudioClientStreamFlags.NoPersist;
            if (_useLoopback)
            {
                streamFlags |= AudioClientStreamFlags.Loopback;
            }

            Marshal.ThrowExceptionForHR(audioClient.Initialize(
                AudioClientShareMode.Shared,
                streamFlags,
                10_000_000,
                0,
                formatPointer,
                IntPtr.Zero
            ));

            var captureClientGuid = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(audioClient.GetService(ref captureClientGuid, out var captureClientObject));
            captureClient = (IAudioCaptureClient)captureClientObject;

            Marshal.ThrowExceptionForHR(audioClient.Start());
            while (!_stopSignal.IsSet)
            {
                DrainPackets(captureClient, writer, waveFormat.nBlockAlign);
                _stopSignal.Wait(15);
            }

            DrainPackets(captureClient, writer, waveFormat.nBlockAlign);
            Marshal.ThrowExceptionForHR(audioClient.Stop());
        }
        catch (Exception error)
        {
            _failure = error;
        }
        finally
        {
            writer?.Dispose();
            if (formatPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(formatPointer);
            }

            ReleaseComObject(captureClient);
            ReleaseComObject(audioClient);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
            CoUninitialize();
        }
    }

    private static void DrainPackets(IAudioCaptureClient captureClient, WaveFileWriter writer, ushort blockAlign)
    {
        while (true)
        {
            Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out var packetLength));
            if (packetLength == 0)
            {
                return;
            }

            Marshal.ThrowExceptionForHR(captureClient.GetBuffer(out var dataPointer, out var framesToRead, out var flags, out _, out _));
            try
            {
                var bytesToWrite = checked((int)(framesToRead * blockAlign));
                if ((flags & AudioCaptureBufferFlags.Silent) != 0)
                {
                    writer.WriteSilence(bytesToWrite);
                }
                else
                {
                    var buffer = new byte[bytesToWrite];
                    Marshal.Copy(dataPointer, buffer, 0, bytesToWrite);
                    writer.Write(buffer);
                }
            }
            finally
            {
                Marshal.ThrowExceptionForHR(captureClient.ReleaseBuffer(framesToRead));
            }
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}

internal sealed class WaveFileWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private long _dataLengthPosition;
    private long _dataStartPosition;
    private bool _disposed;

    public WaveFileWriter(string path, byte[] formatBytes, bool appendToExisting)
    {
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        _writer = new BinaryWriter(_stream);

        if (appendToExisting && _stream.Length > 0)
        {
            if (!TryOpenForAppend(formatBytes))
            {
                throw new InvalidOperationException($"Unable to resume capture because the existing WAV file is incompatible: {path}");
            }

            return;
        }

        _stream.SetLength(0);
        WriteHeader(formatBytes);
    }

    private void WriteHeader(byte[] formatBytes)
    {
        _writer.Write("RIFF"u8.ToArray());
        _writer.Write(0);
        _writer.Write("WAVE"u8.ToArray());
        _writer.Write("fmt "u8.ToArray());
        _writer.Write(formatBytes.Length);
        _writer.Write(formatBytes);
        _writer.Write("data"u8.ToArray());
        _dataLengthPosition = _stream.Position;
        _writer.Write(0);
        _dataStartPosition = _stream.Position;
    }

    private bool TryOpenForAppend(byte[] expectedFormatBytes)
    {
        _stream.Position = 0;
        using var reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("RIFF"u8))
        {
            return false;
        }

        _ = reader.ReadInt32();
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("WAVE"u8))
        {
            return false;
        }

        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("fmt "u8))
        {
            return false;
        }

        var formatLength = reader.ReadInt32();
        var existingFormatBytes = reader.ReadBytes(formatLength);
        if (formatLength != expectedFormatBytes.Length || !existingFormatBytes.AsSpan().SequenceEqual(expectedFormatBytes))
        {
            return false;
        }

        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("data"u8))
        {
            return false;
        }

        _dataLengthPosition = _stream.Position;
        _ = reader.ReadInt32();
        _dataStartPosition = _stream.Position;
        _stream.Position = _stream.Length;
        return true;
    }

    public void Write(byte[] buffer)
    {
        _writer.Write(buffer);
        UpdateHeader();
    }

    public void WriteSilence(int length)
    {
        _writer.Write(new byte[length]);
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var currentPosition = _stream.Position;
        var fileLength = _stream.Length;
        var dataLength = checked((int)(fileLength - _dataStartPosition));

        _stream.Position = 4;
        _writer.Write(checked((int)(fileLength - 8)));
        _stream.Position = _dataLengthPosition;
        _writer.Write(dataLength);
        _writer.Flush();
        _stream.Position = currentPosition;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var fileLength = _stream.Length;
        var dataLength = checked((int)(fileLength - _dataStartPosition));
        _stream.Position = 4;
        _writer.Write(checked((int)(fileLength - 8)));
        _stream.Position = _dataLengthPosition;
        _writer.Write(dataLength);
        _writer.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[Flags]
internal enum AudioClientStreamFlags : uint
{
    None = 0x00000000,
    Loopback = 0x00020000,
    NoPersist = 0x00080000
}

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1
}

[Flags]
internal enum AudioCaptureBufferFlags : uint
{
    None = 0,
    DataDiscontinuity = 0x1,
    Silent = 0x2,
    TimestampError = 0x4
}

internal enum AudioDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2
}

internal enum AudioRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(AudioDataFlow dataFlow, uint stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(AudioDataFlow dataFlow, AudioRole role, out IMMDevice endpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int clsContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    int OpenPropertyStore(int storageAccessMode, out IntPtr properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out uint state);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    int Initialize(AudioClientShareMode shareMode, AudioClientStreamFlags streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr format, IntPtr sessionGuid);
    int GetBufferSize(out uint bufferSize);
    int GetStreamLatency(out long latency);
    int GetCurrentPadding(out uint currentPadding);
    int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, out IntPtr closestMatch);
    int GetMixFormat(out IntPtr deviceFormat);
    int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
    int Start();
    int Stop();
    int Reset();
    int SetEventHandle(IntPtr eventHandle);
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    int GetBuffer(out IntPtr data, out uint framesToRead, out AudioCaptureBufferFlags flags, out ulong devicePosition, out ulong qpcPosition);
    int ReleaseBuffer(uint framesRead);
    int GetNextPacketSize(out uint packetSize);
}
