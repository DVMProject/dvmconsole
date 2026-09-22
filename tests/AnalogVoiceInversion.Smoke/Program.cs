using dvmconsole;

const int sampleRate = 8000;
const int inputFrequency = 700;
const int sampleCount = sampleRate * 2;

if (AnalogVoiceInversion.TryGetFrequency(0, out _) || AnalogVoiceInversion.TryGetFrequency(1, out _) ||
    AnalogVoiceInversion.TryGetFrequency(17, out _))
    throw new Exception("Unsupported codes must not resolve to frequencies.");

for (int code = 2; code <= 16; code++)
{
    if (!AnalogVoiceInversion.TryGetFrequency(code, out int inversionFrequency))
        throw new Exception($"Code {code} is missing.");

    short[] samples = new short[sampleCount];
    for (int i = 0; i < samples.Length; i++)
        samples[i] = (short)Math.Round(8000 * Math.Cos(2 * Math.PI * inputFrequency * i / sampleRate));

    var transmitter = new AnalogVoiceInversion(code);
    for (int offset = 0; offset < samples.Length; offset += 160)
        transmitter.Process(samples.AsSpan(offset, 160));

    double scrambled = ToneLevel(samples, inversionFrequency - inputFrequency);
    if (scrambled < 3000)
        throw new Exception($"Code {code} did not invert {inputFrequency} Hz to {inversionFrequency - inputFrequency} Hz ({scrambled:F0}).");

    var receiver = new AnalogVoiceInversion(code);
    for (int offset = 0; offset < samples.Length; offset += 160)
        receiver.Process(samples.AsSpan(offset, 160));

    double recovered = ToneLevel(samples, inputFrequency);
    if (recovered < 1500)
        throw new Exception($"Code {code} did not recover {inputFrequency} Hz ({recovered:F0}).");
}

short[] mismatched = new short[sampleCount];
for (int i = 0; i < mismatched.Length; i++)
    mismatched[i] = (short)Math.Round(8000 * Math.Cos(2 * Math.PI * inputFrequency * i / sampleRate));
var code2 = new AnalogVoiceInversion(2);
var code9 = new AnalogVoiceInversion(9);
for (int offset = 0; offset < mismatched.Length; offset += 160)
{
    code2.Process(mismatched.AsSpan(offset, 160));
    code9.Process(mismatched.AsSpan(offset, 160));
}
if (ToneLevel(mismatched, 1100) < 1500 || ToneLevel(mismatched, inputFrequency) > 500)
    throw new Exception("Mismatched scrambler codes unexpectedly recovered clear audio.");

Console.WriteLine("Analog voice-inversion smoke test passed for codes 2-16.");

static double ToneLevel(short[] samples, int frequency)
{
    double real = 0;
    double imaginary = 0;
    for (int i = sampleRate; i < sampleCount; i++)
    {
        double phase = 2 * Math.PI * frequency * i / sampleRate;
        real += samples[i] * Math.Cos(phase);
        imaginary += samples[i] * Math.Sin(phase);
    }
    return 2 * Math.Sqrt(real * real + imaginary * imaginary) / sampleRate;
}
