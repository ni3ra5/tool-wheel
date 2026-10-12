import AppKit
import AVFoundation

/// The knob's detent sound: a short, low "thock" synthesised once at startup. A decaying ~90 Hz body with a
/// muffled transient on top, so it reads as a heavy mechanical click rather than a bright tick.
final class Clicker {
    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let buffer: AVAudioPCMBuffer
    let volume: Float = 0.15
    /// Spinning fast crosses many detents per frame; one click per this interval keeps clicks from piling up.
    let minInterval: TimeInterval = 0.05
    private var lastClick = Date.distantPast
    /// Starting the engine wakes the audio hardware, which blocks for ~0.3 s; doing it here lets the wheel show at once.
    private let audio = DispatchQueue(label: "ToolWheel.audio")

    init() {
        let rate = 44_100.0
        let format = AVAudioFormat(standardFormatWithSampleRate: rate, channels: 1)!
        let frames = AVAudioFrameCount(rate * 0.045)  // shorter than minInterval, so clicks never overlap
        buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frames)!
        buffer.frameLength = frames
        let out = buffer.floatChannelData![0]
        var noise: Float = 0
        for i in 0..<Int(frames) {
            let t = Float(i) / Float(rate)
            let attack = min(1, t / 0.0005)                   // avoids a pop at the start
            let release = min(1, (0.045 - t) / 0.01)          // and at the end
            let body = 0.55 * sin(2 * .pi * 92 * t) * exp(-t / 0.014)
                     + 0.25 * sin(2 * .pi * 220 * t) * exp(-t / 0.006)
            noise += 0.25 * (Float.random(in: -1...1) - noise)  // one-pole low-pass: muffled, not hissy
            let transient = 0.35 * noise * exp(-t / 0.0015)
            out[i] = attack * release * (body + transient) * volume
        }
        engine.attach(player)
        engine.connect(player, to: engine.mainMixerNode, format: format)
    }

    /// Audio runs only while the wheel is open.
    func start() {
        audio.async { [self] in
            if (try? engine.start()) != nil { player.play() }
        }
    }

    func stop() {
        audio.async { [self] in
            player.stop()
            engine.pause()
        }
    }

    func click() {
        guard Date().timeIntervalSince(lastClick) >= minInterval else { return }
        let at = Date()
        lastClick = at
        audio.async { [self] in
            // A click made while the engine was still starting would sound late; drop it instead.
            guard engine.isRunning, Date().timeIntervalSince(at) < minInterval else { return }
            player.scheduleBuffer(buffer, at: nil, options: .interrupts)
        }
        NSHapticFeedbackManager.defaultPerformer.perform(.generic, performanceTime: .now)  // felt only on a Force Touch trackpad
    }
}
