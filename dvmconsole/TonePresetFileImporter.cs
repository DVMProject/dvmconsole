// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace dvmconsole
{
    internal sealed class ImportedTonePreset
    {
        public string DisplayName { get; set; } = "Imported Tone";
        public List<ImportedTonePresetStep> Steps { get; } = new List<ImportedTonePresetStep>();
        public List<string> Warnings { get; } = new List<string>();
    }

    internal sealed class ImportedTonePresetStep
    {
        public bool IsHold { get; set; }
        public double FrequencyHz { get; set; }
        public double DurationSeconds { get; set; }
    }

    internal static class TonePresetFileImporter
    {
        private const int MAX_IMPORTED_STEPS = 1024;
        private const int DEFAULT_TEMPO_MICROSECONDS_PER_QUARTER = 500000;
        private const double MIN_TONE_FREQUENCY_HZ = 1.0;
        private const double MAX_TONE_FREQUENCY_HZ = 4000.0;
        private const double MIN_HOLD_GAP_SECONDS = 0.01;
        private const double DURATION_EPSILON_SECONDS = 0.0001;

        public static ImportedTonePreset Import(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Choose a tone file to import.", nameof(filePath));

            string extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".mid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".midi", StringComparison.OrdinalIgnoreCase))
                return ImportMidi(filePath);

            throw new NotSupportedException("Only .mid and .midi tone imports are supported.");
        }

        private static ImportedTonePreset ImportMidi(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            using BinaryReader reader = new BinaryReader(stream);

            string headerId = ReadChunkId(reader);
            if (!string.Equals(headerId, "MThd", StringComparison.Ordinal))
                throw new InvalidDataException("This does not look like a standard MIDI file.");

            int headerLength = ReadInt32BigEndian(reader);
            if (headerLength < 6)
                throw new InvalidDataException("The MIDI header is invalid.");

            int format = ReadUInt16BigEndian(reader);
            int trackCount = ReadUInt16BigEndian(reader);
            int division = ReadUInt16BigEndian(reader);
            if (headerLength > 6)
                reader.BaseStream.Seek(headerLength - 6, SeekOrigin.Current);

            if (format > 1)
                throw new NotSupportedException("Only standard single-song MIDI files are supported.");

            if ((division & 0x8000) != 0)
                throw new NotSupportedException("SMPTE-time MIDI files are not supported for tone import.");

            if (trackCount <= 0)
                throw new InvalidDataException("The MIDI file does not contain any tracks.");

            List<MidiNoteEvent> noteEvents = new List<MidiNoteEvent>();
            List<MidiTempoEvent> tempoEvents = new List<MidiTempoEvent>();
            int ticksPerQuarter = division;
            int tracksRead = 0;
            int eventSequence = 0;

            while (reader.BaseStream.Position < reader.BaseStream.Length && tracksRead < trackCount)
            {
                string chunkId = ReadChunkId(reader);
                int chunkLength = ReadInt32BigEndian(reader);
                if (chunkLength < 0 || reader.BaseStream.Position + chunkLength > reader.BaseStream.Length)
                    throw new InvalidDataException("A MIDI track chunk is invalid.");

                byte[] chunkData = reader.ReadBytes(chunkLength);
                if (!string.Equals(chunkId, "MTrk", StringComparison.Ordinal))
                    continue;

                ParseTrack(chunkData, noteEvents, tempoEvents, ref eventSequence);
                tracksRead++;
            }

            if (noteEvents.Count == 0)
                throw new InvalidDataException("No note events were found in the MIDI file.");

            List<MidiNoteInterval> intervals = BuildNoteIntervals(noteEvents);
            if (intervals.Count == 0)
                throw new InvalidDataException("No complete notes were found in the MIDI file.");

            ImportedTonePreset imported = new ImportedTonePreset
            {
                DisplayName = Path.GetFileNameWithoutExtension(filePath)
            };

            BuildToneSteps(imported, intervals, tempoEvents, ticksPerQuarter);
            if (imported.Steps.Count == 0)
                throw new InvalidDataException("No usable tones could be imported from the MIDI file.");

            return imported;
        }

        private static void ParseTrack(
            byte[] chunkData,
            List<MidiNoteEvent> noteEvents,
            List<MidiTempoEvent> tempoEvents,
            ref int eventSequence)
        {
            using MemoryStream stream = new MemoryStream(chunkData);
            using BinaryReader reader = new BinaryReader(stream);

            long tick = 0;
            int runningStatus = 0;

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                tick += ReadVariableLengthQuantity(reader);

                int firstByte = reader.ReadByte();
                int status;
                int firstDataByte = -1;
                if ((firstByte & 0x80) == 0)
                {
                    if (runningStatus == 0)
                        throw new InvalidDataException("A MIDI event used running status before a status byte was defined.");

                    status = runningStatus;
                    firstDataByte = firstByte;
                }
                else
                {
                    status = firstByte;
                    if (status >= 0x80 && status <= 0xEF)
                        runningStatus = status;
                }

                if (status == 0xFF)
                {
                    int metaType = reader.ReadByte();
                    int length = ReadVariableLengthQuantity(reader);
                    byte[] data = reader.ReadBytes(length);

                    if (metaType == 0x51 && data.Length == 3)
                    {
                        int tempo = (data[0] << 16) | (data[1] << 8) | data[2];
                        tempoEvents.Add(new MidiTempoEvent { Tick = tick, MicrosecondsPerQuarter = tempo });
                    }

                    if (metaType == 0x2F)
                        break;

                    runningStatus = 0;
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int length = ReadVariableLengthQuantity(reader);
                    reader.BaseStream.Seek(length, SeekOrigin.Current);
                    runningStatus = 0;
                    continue;
                }

                int messageType = status & 0xF0;
                if (status < 0x80 || status > 0xEF)
                {
                    SkipSystemMessageData(reader, status, firstDataByte);
                    runningStatus = 0;
                    continue;
                }

                int dataLength = GetChannelMessageDataLength(messageType);
                int data1 = firstDataByte >= 0 ? firstDataByte : reader.ReadByte();
                int data2 = dataLength == 2 ? reader.ReadByte() : 0;

                if (messageType != 0x80 && messageType != 0x90)
                    continue;

                bool isNoteOn = messageType == 0x90 && data2 > 0;
                noteEvents.Add(new MidiNoteEvent
                {
                    Tick = tick,
                    Channel = status & 0x0F,
                    Note = data1,
                    IsNoteOn = isNoteOn,
                    Sequence = eventSequence++
                });
            }
        }

        private static List<MidiNoteInterval> BuildNoteIntervals(List<MidiNoteEvent> noteEvents)
        {
            Dictionary<int, Queue<MidiNoteEvent>> activeNotes = new Dictionary<int, Queue<MidiNoteEvent>>();
            List<MidiNoteInterval> intervals = new List<MidiNoteInterval>();

            foreach (MidiNoteEvent noteEvent in noteEvents
                .OrderBy(noteEvent => noteEvent.Tick)
                .ThenBy(noteEvent => noteEvent.IsNoteOn ? 1 : 0)
                .ThenBy(noteEvent => noteEvent.Sequence))
            {
                int key = (noteEvent.Channel << 8) | noteEvent.Note;
                if (noteEvent.IsNoteOn)
                {
                    if (!activeNotes.TryGetValue(key, out Queue<MidiNoteEvent> starts))
                    {
                        starts = new Queue<MidiNoteEvent>();
                        activeNotes[key] = starts;
                    }

                    starts.Enqueue(noteEvent);
                    continue;
                }

                if (!activeNotes.TryGetValue(key, out Queue<MidiNoteEvent> activeStarts) || activeStarts.Count == 0)
                    continue;

                MidiNoteEvent start = activeStarts.Dequeue();
                if (noteEvent.Tick <= start.Tick)
                    continue;

                intervals.Add(new MidiNoteInterval
                {
                    StartTick = start.Tick,
                    EndTick = noteEvent.Tick,
                    Note = noteEvent.Note
                });
            }

            return intervals;
        }

        private static void BuildToneSteps(
            ImportedTonePreset imported,
            List<MidiNoteInterval> intervals,
            List<MidiTempoEvent> tempoEvents,
            int ticksPerQuarter)
        {
            List<MidiToneInterval> timeline = intervals
                .Select(interval => new MidiToneInterval
                {
                    StartSeconds = TicksToSeconds(interval.StartTick, tempoEvents, ticksPerQuarter),
                    EndSeconds = TicksToSeconds(interval.EndTick, tempoEvents, ticksPerQuarter),
                    FrequencyHz = MidiNoteToFrequency(interval.Note)
                })
                .Where(interval => interval.EndSeconds > interval.StartSeconds)
                .OrderBy(interval => interval.StartSeconds)
                .ThenBy(interval => interval.EndSeconds)
                .ToList();

            bool flattenedOverlaps = false;
            bool truncated = false;
            bool stretchedShortTones = false;
            double currentSeconds = 0;

            foreach (MidiToneInterval interval in timeline)
            {
                if (imported.Steps.Count >= MAX_IMPORTED_STEPS)
                {
                    truncated = true;
                    break;
                }

                if (interval.StartSeconds < currentSeconds)
                    flattenedOverlaps = true;

                double startSeconds = Math.Max(interval.StartSeconds, currentSeconds);
                if (interval.EndSeconds <= startSeconds + DURATION_EPSILON_SECONDS)
                    continue;

                double gapSeconds = interval.StartSeconds - currentSeconds;
                if (gapSeconds >= SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS)
                    AddStepChunks(imported, true, 0, gapSeconds, ref truncated, ref stretchedShortTones);
                else if (gapSeconds > MIN_HOLD_GAP_SECONDS)
                    flattenedOverlaps = true;

                AddStepChunks(imported, false, interval.FrequencyHz, interval.EndSeconds - startSeconds, ref truncated, ref stretchedShortTones);
                currentSeconds = interval.EndSeconds;
            }

            if (flattenedOverlaps)
                imported.Warnings.Add("Overlapping notes and tiny rests were flattened to fit the tone-stack format.");
            if (stretchedShortTones)
                imported.Warnings.Add("Very short notes were stretched to the minimum tone duration.");
            if (truncated)
                imported.Warnings.Add($"Import was limited to {MAX_IMPORTED_STEPS} tone steps.");
        }

        private static void AddStepChunks(
            ImportedTonePreset imported,
            bool isHold,
            double frequencyHz,
            double durationSeconds,
            ref bool truncated,
            ref bool stretchedShortTones)
        {
            if (durationSeconds <= DURATION_EPSILON_SECONDS)
                return;

            if (isHold && durationSeconds < SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS)
                return;

            if (!isHold && durationSeconds < SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS)
            {
                durationSeconds = SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS;
                stretchedShortTones = true;
            }

            double remainingSeconds = durationSeconds;
            while (remainingSeconds > DURATION_EPSILON_SECONDS && imported.Steps.Count < MAX_IMPORTED_STEPS)
            {
                double chunkSeconds = Math.Min(remainingSeconds, SettingsManager.TONE_PRESET_MAX_DURATION_SECONDS);
                if (chunkSeconds < SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS)
                {
                    ImportedTonePresetStep lastStep = imported.Steps.LastOrDefault();
                    if (lastStep != null &&
                        lastStep.IsHold == isHold &&
                        Math.Abs(lastStep.FrequencyHz - frequencyHz) < 0.0001 &&
                        lastStep.DurationSeconds + chunkSeconds <= SettingsManager.TONE_PRESET_MAX_DURATION_SECONDS)
                    {
                        lastStep.DurationSeconds += chunkSeconds;
                    }
                    else if (!isHold)
                    {
                        imported.Steps.Add(new ImportedTonePresetStep
                        {
                            IsHold = false,
                            FrequencyHz = ClampToneFrequency(frequencyHz),
                            DurationSeconds = SettingsManager.TONE_PRESET_MIN_DURATION_SECONDS
                        });
                        stretchedShortTones = true;
                    }

                    break;
                }

                imported.Steps.Add(new ImportedTonePresetStep
                {
                    IsHold = isHold,
                    FrequencyHz = isHold ? 0 : ClampToneFrequency(frequencyHz),
                    DurationSeconds = chunkSeconds
                });

                remainingSeconds -= chunkSeconds;
            }

            if (remainingSeconds > DURATION_EPSILON_SECONDS && imported.Steps.Count >= MAX_IMPORTED_STEPS)
                truncated = true;
        }

        private static double TicksToSeconds(long targetTick, List<MidiTempoEvent> tempoEvents, int ticksPerQuarter)
        {
            double seconds = 0;
            long previousTick = 0;
            int currentTempo = DEFAULT_TEMPO_MICROSECONDS_PER_QUARTER;

            foreach (MidiTempoEvent tempoEvent in tempoEvents
                .Where(tempoEvent => tempoEvent.Tick <= targetTick)
                .OrderBy(tempoEvent => tempoEvent.Tick))
            {
                if (tempoEvent.Tick > previousTick)
                {
                    seconds += TicksToDurationSeconds(tempoEvent.Tick - previousTick, currentTempo, ticksPerQuarter);
                    previousTick = tempoEvent.Tick;
                }

                currentTempo = tempoEvent.MicrosecondsPerQuarter;
            }

            if (targetTick > previousTick)
                seconds += TicksToDurationSeconds(targetTick - previousTick, currentTempo, ticksPerQuarter);

            return seconds;
        }

        private static double TicksToDurationSeconds(long ticks, int tempoMicrosecondsPerQuarter, int ticksPerQuarter)
        {
            if (ticks <= 0 || ticksPerQuarter <= 0)
                return 0;

            return ticks * (tempoMicrosecondsPerQuarter / 1000000.0) / ticksPerQuarter;
        }

        private static double MidiNoteToFrequency(int note)
        {
            return ClampToneFrequency(440.0 * Math.Pow(2.0, (note - 69) / 12.0));
        }

        private static double ClampToneFrequency(double frequencyHz)
        {
            return Math.Clamp(frequencyHz, MIN_TONE_FREQUENCY_HZ, MAX_TONE_FREQUENCY_HZ);
        }

        private static int GetChannelMessageDataLength(int messageType)
        {
            return messageType == 0xC0 || messageType == 0xD0 ? 1 : 2;
        }

        private static void SkipSystemMessageData(BinaryReader reader, int status, int firstDataByte)
        {
            int dataLength = status switch
            {
                0xF1 => 1,
                0xF2 => 2,
                0xF3 => 1,
                _ => 0
            };

            if (firstDataByte >= 0)
                dataLength--;

            if (dataLength > 0)
                reader.BaseStream.Seek(dataLength, SeekOrigin.Current);
        }

        private static string ReadChunkId(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
                throw new EndOfStreamException("Unexpected end of MIDI file.");

            return Encoding.ASCII.GetString(bytes);
        }

        private static int ReadInt32BigEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
                throw new EndOfStreamException("Unexpected end of MIDI file.");

            return (bytes[0] << 24) |
                   (bytes[1] << 16) |
                   (bytes[2] << 8) |
                   bytes[3];
        }

        private static int ReadUInt16BigEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (bytes.Length != 2)
                throw new EndOfStreamException("Unexpected end of MIDI file.");

            return (bytes[0] << 8) | bytes[1];
        }

        private static int ReadVariableLengthQuantity(BinaryReader reader)
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                int currentByte = reader.ReadByte();
                value = (value << 7) | (currentByte & 0x7F);
                if ((currentByte & 0x80) == 0)
                    return value;
            }

            throw new InvalidDataException("A MIDI variable-length value is invalid.");
        }

        private sealed class MidiNoteEvent
        {
            public long Tick { get; set; }
            public int Channel { get; set; }
            public int Note { get; set; }
            public bool IsNoteOn { get; set; }
            public int Sequence { get; set; }
        }

        private sealed class MidiNoteInterval
        {
            public long StartTick { get; set; }
            public long EndTick { get; set; }
            public int Note { get; set; }
        }

        private sealed class MidiToneInterval
        {
            public double StartSeconds { get; set; }
            public double EndSeconds { get; set; }
            public double FrequencyHz { get; set; }
        }

        private sealed class MidiTempoEvent
        {
            public long Tick { get; set; }
            public int MicrosecondsPerQuarter { get; set; } = DEFAULT_TEMPO_MICROSECONDS_PER_QUARTER;
        }
    }
}
