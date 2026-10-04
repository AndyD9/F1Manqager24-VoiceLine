# Generates WAV files with a Windows SAPI voice.
# Input: UTF-8 TSV, one "<out.wav><TAB><text>" per line.
param(
    [Parameter(Mandatory)] [string] $List,
    [string] $Voice = "Microsoft Zira Desktop",
    [int] $Rate = 0,
    [int] $SampleRate = 24000
)
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice($Voice)
$synth.Rate = $Rate
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo($SampleRate,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$n = 0
foreach ($line in [System.IO.File]::ReadAllLines($List, [System.Text.Encoding]::UTF8)) {
    $parts = $line.Split("`t", 2)
    if ($parts.Count -ne 2) { continue }
    $synth.SetOutputToWaveFile($parts[0], $fmt)
    $synth.Speak($parts[1])
    $n++
}
$synth.SetOutputToNull()
$synth.Dispose()
Write-Output "$n file(s) generated"
