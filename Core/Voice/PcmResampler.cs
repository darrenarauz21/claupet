namespace Claupet;

/// <summary>
/// Pasa audio mono de una frecuencia a otra (16 kHz, si no se dice), por partes: al bajarla, un promedio móvil del
/// ancho de la reducción (contra el aliasing); después, interpolación lineal. Guarda lo justo entre partes.
/// </summary>
sealed class PcmResampler
{
    readonly double _step;
    readonly int _width;
    readonly float[] _window;
    readonly List<float> _pending = new();
    double _position;
    float _sum;
    int _filled, _at;

    public PcmResampler(int from, int to = 16000)
    {
        if (from <= 0 || to <= 0) throw new ArgumentOutOfRangeException(nameof(from));
        _step = from / (double)to;
        _width = Math.Max(1, (int)Math.Round(_step));
        _window = new float[_width];
    }

    /// <summary>En 16 bits, como lo graba el micrófono.</summary>
    public short[] Push(ReadOnlySpan<float> mono) =>
        Array.ConvertAll(PushFloat(mono), value => (short)Math.Clamp(value * 32767f, -32768f, 32767f));

    /// <summary>De -1 a 1, como lo toma el parlante.</summary>
    public float[] PushFloat(ReadOnlySpan<float> mono)
    {
        foreach (var sample in mono)
        {
            _sum += sample - _window[_at];
            _window[_at] = sample;
            _at = (_at + 1) % _width;
            if (_filled < _width) _filled++;
            _pending.Add(_sum / _filled);
        }
        var output = new List<float>((int)(mono.Length / _step) + 2);
        while (_position + 1 < _pending.Count)
        {
            var i = (int)_position;
            var f = (float)(_position - i);
            var value = _pending[i] * (1 - f) + _pending[i + 1] * f;
            output.Add(value);
            _position += _step;
        }
        var drop = Math.Min((int)_position, _pending.Count);
        _pending.RemoveRange(0, drop);
        _position -= drop;
        return output.ToArray();
    }
}
