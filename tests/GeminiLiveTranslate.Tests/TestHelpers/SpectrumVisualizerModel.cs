using System;
using System.Collections.ObjectModel;
using GeminiLiveTranslate.Core.Models;

namespace GeminiLiveTranslate.Tests.TestHelpers;

/// <summary>
/// Opaque-box model validating 36-band audio spectrum waveform visualizer,
/// decay physics, and bilingual translation history card stream.
/// </summary>
public class SpectrumVisualizerModel
{
    public const int BandCount = 36;
    public const float DefaultDecayRate = 0.08f;

    private readonly float[] _currentLevels = new float[BandCount];
    private readonly ObservableCollection<TranslationTurn> _historyCards = new();

    public float DecayRate { get; set; } = DefaultDecayRate;
    public IReadOnlyList<float> CurrentLevels => _currentLevels;
    public ObservableCollection<TranslationTurn> HistoryCards => _historyCards;

    /// <summary>
    /// Updates spectrum bands with new FFT amplitudes, applying decay physics and smoothing.
    /// </summary>
    public void UpdateSpectrum(float[]? newBands)
    {
        if (newBands == null || newBands.Length == 0)
        {
            DecayAll();
            return;
        }

        int count = Math.Min(BandCount, newBands.Length);
        for (int i = 0; i < count; i++)
        {
            float target = newBands[i];
            if (float.IsNaN(target) || float.IsInfinity(target))
            {
                target = 0.0f;
            }

            target = Math.Clamp(target, 0.0f, 1.0f);

            if (target >= _currentLevels[i])
            {
                // Immediate attack
                _currentLevels[i] = target;
            }
            else
            {
                // Smooth decay
                _currentLevels[i] = Math.Max(0.0f, _currentLevels[i] - DecayRate);
            }
        }
    }

    /// <summary>
    /// Applies decay step across all bands (e.g. during silence or frame tick).
    /// </summary>
    public void DecayAll()
    {
        for (int i = 0; i < BandCount; i++)
        {
            _currentLevels[i] = Math.Max(0.0f, _currentLevels[i] - DecayRate);
        }
    }

    /// <summary>
    /// Adds a completed translation turn card to the stream.
    /// </summary>
    public void AddTurnCard(TranslationTurn turn)
    {
        if (turn != null)
        {
            _historyCards.Add(turn);
        }
    }

    /// <summary>
    /// Clears all history cards.
    /// </summary>
    public void ClearHistory()
    {
        _historyCards.Clear();
    }
}
