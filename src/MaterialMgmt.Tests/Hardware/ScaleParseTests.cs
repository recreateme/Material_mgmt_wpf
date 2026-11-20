using MaterialMgmt.Infrastructure.Hardware;

namespace MaterialMgmt.Tests.Hardware;

public class ScaleParseTests
{
    private static ushort Crc16(byte[] data, int len)
    {
        ushort crc = 0xFFFF;
        for (var i = 0; i < len; i++)
        {
            crc ^= data[i];
            for (var b = 0; b < 8; b++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>构造 13 字节响应：01 03 08 | 重量(4B 大端) | 保留 | 小数位 | 保留 | 状态 | CRC(低,高)。</summary>
    private static byte[] Frame(int rawWeight, byte dot, byte status, bool badCrc = false)
    {
        var f = new byte[13];
        f[0] = 0x01; f[1] = 0x03; f[2] = 0x08;
        f[3] = (byte)(rawWeight >> 24); f[4] = (byte)(rawWeight >> 16);
        f[5] = (byte)(rawWeight >> 8); f[6] = (byte)rawWeight;
        f[8] = dot;
        f[10] = status;
        var crc = Crc16(f, 11);
        if (badCrc) crc ^= 0x0001;
        f[11] = (byte)(crc & 0xFF);
        f[12] = (byte)(crc >> 8);
        return f;
    }

    [Fact]
    public void Parse_ValidFrame_StableWeight()
    {
        var r = ScaleWorker.Parse(Frame(12345, 3, 0x02));
        Assert.NotNull(r);
        Assert.Equal(12.345, r!.Value.Weight, 6);
        Assert.True(r.Value.Stable);
        Assert.False(r.Value.Overload);
    }

    [Fact]
    public void Parse_NegativeWeight_AndOverloadFlag()
    {
        var r = ScaleWorker.Parse(Frame(-500, 2, 0x04));
        Assert.NotNull(r);
        Assert.Equal(-5.0, r!.Value.Weight, 6);
        Assert.False(r.Value.Stable);
        Assert.True(r.Value.Overload);
    }

    [Fact]
    public void Parse_BadCrc_ReturnsNull() => Assert.Null(ScaleWorker.Parse(Frame(100, 1, 0x02, badCrc: true)));

    [Fact]
    public void Parse_ShortOrWrongHeader_ReturnsNull()
    {
        Assert.Null(ScaleWorker.Parse(new byte[5]));
        var f = Frame(100, 1, 0x02);
        f[0] = 0x02;
        Assert.Null(ScaleWorker.Parse(f));
    }
}
