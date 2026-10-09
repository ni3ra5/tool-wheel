import AppKit
import ApplicationServices
import Carbon
import SwiftUI

// MARK: - Config

/// One wheel entry: an app (or, if set by hand in tools.json, any file, folder or URL) to open.
struct Tool: Codable, Hashable, Identifiable {
    var name: String
    var path: String?  // optional only so old entries without one (removed custom commands) still decode
    var color: String?  // hex like "#34C759": a colour band along the slice's outer edge; nil = none

    /// Same app = same tool regardless of display name, so it can't be added to the wheel twice.
    var id: String { path ?? name }

    var expandedPath: String? { path.map { ($0 as NSString).expandingTildeInPath } }

    var icon: NSImage {
        if let p = expandedPath, !p.contains("://") {
            return NSWorkspace.shared.icon(forFile: URL(fileURLWithPath: p).resolvingSymlinksInPath().path)  // no alias badge
        }
        return NSImage(systemSymbolName: "globe", accessibilityDescription: name)!
    }

    func launch() {
        guard let p = expandedPath, let url = p.contains("://") ? URL(string: p) : URL(fileURLWithPath: p) else { return }
        NSWorkspace.shared.open(url)
    }
}

/// Slot colours offered in Settings. Stored per tool as hex, so the colour moves with the app when reordered.
let slotColors: [(name: String, hex: String)] = [
    ("Orange", "#FF3C00"), ("Amber", "#FFB300"), ("Green", "#34C759"), ("Teal", "#30B0C7"),
    ("Blue", "#0A84FF"), ("Purple", "#AF52DE"), ("Pink", "#FF2D55"), ("Graphite", "#8E8E93"),
]
let bandWidth: CGFloat = 4  // colour band along a slice's outer edge

extension Color {
    /// "#RRGGBB"; nil if it isn't one.
    init?(hex: String) {
        let digits = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard digits.count == 6, let v = UInt32(digits, radix: 16) else { return nil }
        self.init(red: Double(v >> 16 & 0xFF) / 255, green: Double(v >> 8 & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }
}

let configURL = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent(".config/toolwheel/tools.json")

/// `wheel` is what shows on the wheel, in order.
struct Config: Codable {
    var wheel: [Tool]
    var shortcut: UInt?  // modifier flags to hold; nil means the default
    var mouseButton: Int?  // a side button (4 or 5) held along with those keys, which may then be none
    var toggleKey: Int?  // the on/off shortcut: a key code…
    var toggleModifiers: UInt?  // …and the modifier flags held with it
    var releaseToOpen: Bool?  // true: letting go of the shortcut opens the hovered tool; nil/false: click to open

    var button: Int? { mouseButton == 4 || mouseButton == 5 ? mouseButton : nil }
    /// The on/off shortcut, or nil for none. It needs at least one modifier, or it would swallow a key everywhere.
    var toggle: (modifiers: NSEvent.ModifierFlags, key: Int)? {
        guard let toggleKey, let toggleModifiers, toggleModifiers != 0 else { return nil }
        return (NSEvent.ModifierFlags(rawValue: toggleModifiers), toggleKey)
    }
    /// No keys and no button would mean "always held"; that falls back to the default.
    var trigger: NSEvent.ModifierFlags {
        guard let shortcut, shortcut != 0 || button != nil else { return Shortcut.standard }
        return NSEvent.ModifierFlags(rawValue: shortcut)
    }
    var isDefaultTrigger: Bool { trigger == Shortcut.standard && button == nil }
}

/// The wheel opens while a combination of modifier keys is held (no other key, so no extra permission is needed),
/// optionally with a mouse side button.
enum Shortcut {
    static let keys: NSEvent.ModifierFlags = [.control, .option, .shift, .command]
    static let standard: NSEvent.ModifierFlags = [.control, .option, .command]
    static var current = standard
    static var button: Int?
    static var toggle: (modifiers: NSEvent.ModifierFlags, key: Int)?
    static var recording = false  // Settings is capturing a new shortcut; don't open the wheel meanwhile
    static var settingsOpen = false  // the wheel's shortcut rests while Settings is open
    static var releaseToOpen = false

    static func symbols(_ flags: NSEvent.ModifierFlags) -> String {
        [(NSEvent.ModifierFlags.control, "⌃"), (.option, "⌥"), (.shift, "⇧"), (.command, "⌘")]
            .filter { flags.contains($0.0) }.map(\.1).joined()
    }

    static func describe(_ flags: NSEvent.ModifierFlags, _ button: Int?) -> String {
        [symbols(flags), button.map { "Mouse \($0)" } ?? ""].filter { !$0.isEmpty }.joined(separator: " ")
    }

    /// The key's name in the current keyboard layout ("P", "F5", "Space").
    static func keyName(_ code: Int) -> String {
        let named: [Int: String] = [
            kVK_Return: "↩", kVK_Tab: "⇥", kVK_Space: "Space", kVK_Delete: "⌫", kVK_ForwardDelete: "⌦",
            kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_DownArrow: "↓", kVK_UpArrow: "↑",
            kVK_Home: "↖", kVK_End: "↘", kVK_PageUp: "⇞", kVK_PageDown: "⇟",
            kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6",
            kVK_F7: "F7", kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12",
        ]
        if let name = named[code] { return name }
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let data = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return "#\(code)" }
        let layout = Unmanaged<CFData>.fromOpaque(data).takeUnretainedValue()
        var deadKeys: UInt32 = 0
        var chars = [UniChar](repeating: 0, count: 4)
        var length = 0
        let status = UCKeyTranslate(UnsafeRawPointer(CFDataGetBytePtr(layout)).assumingMemoryBound(to: UCKeyboardLayout.self),
                                    UInt16(code), UInt16(kUCKeyActionDisplay), 0, UInt32(LMGetKbdType()),
                                    OptionBits(kUCKeyTranslateNoDeadKeysMask), &deadKeys, chars.count, &length, &chars)
        return status == 0 && length > 0 ? String(utf16CodeUnits: chars, count: length).uppercased() : "#\(code)"
    }
}

/// The on/off shortcut, registered as a system hotkey (Carbon's, which needs no permission). Off lasts until it's
/// pressed again or the app restarts. Unregistered while Settings records a shortcut, so pressing it there records it
/// instead of turning the wheel off.
enum OnOff {
    static var on = true
    static var pressed: () -> Void = {}
    private static var hotKey: EventHotKeyRef?
    private static var handler: EventHandlerRef?
    private static var registered: String?  // what's registered now, as "modifiers-key"

    /// Polled with the wheel's tick.
    static func sync() {
        let want = Shortcut.recording ? nil : Shortcut.toggle
        let key = want.map { "\($0.modifiers.rawValue)-\($0.key)" }
        guard key != registered else { return }
        if let hotKey { UnregisterEventHotKey(hotKey) }
        hotKey = nil
        registered = key
        guard let want else { return }
        if handler == nil {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in OnOff.pressed(); return 0 }, 1, &spec, nil, &handler)
        }
        RegisterEventHotKey(UInt32(want.key), carbonModifiers(want.modifiers), EventHotKeyID(signature: 0x5457_4F4E, id: 1),
                            GetApplicationEventTarget(), 0, &hotKey)
    }

    static func carbonModifiers(_ flags: NSEvent.ModifierFlags) -> UInt32 {
        var m = 0
        if flags.contains(.command) { m |= cmdKey }
        if flags.contains(.option) { m |= optionKey }
        if flags.contains(.control) { m |= controlKey }
        if flags.contains(.shift) { m |= shiftKey }
        return UInt32(m)
    }
}

/// "Wheel off" / "Wheel on" for a moment below the cursor when the on/off shortcut is pressed. Same look and timings as
/// Windows' BadgeWindow: a dark pill with an accent (on) or grey (off) dot; fades in 120 ms, holds 900 ms, fades out 250 ms.
struct BadgeView: View {
    let on: Bool

    var body: some View {
        HStack(spacing: 8) {
            Circle().fill(on ? accent : .white.opacity(0.3)).frame(width: 8, height: 8)
            Text(on ? "Wheel on" : "Wheel off")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(.white.opacity(0.9))
        }
        .padding(.leading, 12)
        .padding(.trailing, 14)
        .frame(height: 30)
        .background(Capsule().fill(Color(white: 30.0 / 255).opacity(0.96)))
        .overlay(Capsule().strokeBorder(.white.opacity(0.1), lineWidth: 1))
    }
}

@MainActor
final class Badge {
    let panel: NSPanel
    var generation = 0  // a newer flash cancels the older one's fade-out

    init() {
        panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .popUpMenu
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
    }

    func flash(on: Bool) {
        let view = NSHostingView(rootView: BadgeView(on: on))
        let size = view.fittingSize
        panel.contentView = view
        let mouse = NSEvent.mouseLocation  // AppKit y is up, so 24 pt below the cursor is minus
        panel.setFrame(NSRect(x: (mouse.x - size.width / 2).rounded(), y: (mouse.y - 24 - size.height).rounded(),
                              width: size.width, height: size.height), display: true)
        if !panel.isVisible {
            panel.alphaValue = 0
            panel.orderFrontRegardless()
        }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.12
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            self.panel.animator().alphaValue = 1
        }
        generation += 1
        let mine = generation
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.12 + 0.9) {
            guard mine == self.generation else { return }
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.25
                context.timingFunction = CAMediaTimingFunction(name: .easeIn)
                self.panel.animator().alphaValue = 0
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                if mine == self.generation { self.panel.orderOut(nil) }
            }
        }
    }
}

/// While a side button is the shortcut, an event tap swallows its presses so the app under the cursor doesn't also
/// go Back/Forward. That needs Accessibility permission, asked for only when a side button is picked; without it
/// the wheel still opens (by polling) and the button keeps its usual action.
enum SideButtons {
    static var tap: CFMachPort?
    static var held: Int?  // the side button whose press was swallowed and not yet let go

    /// A swallowed press never shows in pressedMouseButtons, so with the tap in only its own record counts; that also
    /// means the keys must already be held when the button goes down.
    static func down(_ button: Int) -> Bool {
        tap != nil ? held == button : NSEvent.pressedMouseButtons & (1 << (button - 1)) != 0
    }

    /// Puts the tap in once a side button is the shortcut and permission is granted. Stays for the session.
    static func update(prompt: Bool = false) {
        guard Shortcut.button != nil, tap == nil,
              AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": prompt] as CFDictionary) else { return }
        let mask = CGEventMask(1 << CGEventType.otherMouseDown.rawValue) | CGEventMask(1 << CGEventType.otherMouseUp.rawValue)
        tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                eventsOfInterest: mask, callback: { _, type, event, _ in SideButtons.handle(type, event) },
                                userInfo: nil)
        guard let tap else { return }
        CFRunLoopAddSource(CFRunLoopGetMain(), CFMachPortCreateRunLoopSource(nil, tap, 0), .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }

    static func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        let button = Int(event.getIntegerValueField(.mouseEventButtonNumber)) + 1  // 0 is the left button
        let mods = NSEvent.ModifierFlags(rawValue: UInt(event.flags.rawValue)).intersection(Shortcut.keys)
        if type == .otherMouseDown, button == Shortcut.button, OnOff.on, !Shortcut.recording, !Shortcut.settingsOpen,
           mods == Shortcut.current {
            held = button
            return nil
        }
        if type == .otherMouseUp, button == held {
            held = nil
            return nil
        }
        return Unmanaged.passUnretained(event)
    }
}

let defaultConfig = Config(wheel: [
    Tool(name: "Finder", path: "/System/Library/CoreServices/Finder.app"),
    Tool(name: "Safari", path: "/Applications/Safari.app"),
    Tool(name: "Terminal", path: "/System/Applications/Utilities/Terminal.app"),
    Tool(name: "Notes", path: "/System/Applications/Notes.app"),
    Tool(name: "Calculator", path: "/System/Applications/Calculator.app"),
    Tool(name: "Settings", path: "/System/Applications/System Settings.app"),
])

/// Re-read on every open, so edits from Settings or by hand apply without restarting.
func loadConfig() -> Config {
    guard let data = try? Data(contentsOf: configURL) else {
        saveConfig(defaultConfig)
        return defaultConfig
    }
    // Entries without a path were custom commands, which the app no longer has; they're dropped.
    if var config = try? JSONDecoder().decode(Config.self, from: data) {
        config.wheel.removeAll { $0.path == nil }
        return config
    }
    // Older format: a bare list of wheel tools.
    if let wheel = try? JSONDecoder().decode([Tool].self, from: data) {
        return Config(wheel: wheel.filter { $0.path != nil })
    }
    // Unreadable: set it aside rather than let the next save overwrite it.
    let backup = configURL.appendingPathExtension("invalid")
    try? FileManager.default.removeItem(at: backup)
    try? FileManager.default.moveItem(at: configURL, to: backup)
    print("tools.json was invalid; moved to \(backup.path), using defaults")
    return defaultConfig
}

func saveConfig(_ config: Config) {
    try? FileManager.default.createDirectory(at: configURL.deletingLastPathComponent(), withIntermediateDirectories: true)
    let enc = JSONEncoder()
    enc.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
    try? enc.encode(config).write(to: configURL)
}

// MARK: - Geometry

/// Slice under the cursor, or nil over the knob or outside the wheel. dx/dy in AppKit coords (y up).
/// Slice 0 is at 12 o'clock, going clockwise.
func sliceIndex(dx: CGFloat, dy: CGFloat, count: Int,
                inner: CGFloat = centerRadius, outer: CGFloat = outerRadius) -> Int? {
    let distance = hypot(dx, dy)
    guard count > 0, distance > inner, distance <= outer else { return nil }
    var angle = atan2(dx, dy)  // clockwise from north
    if angle < 0 { angle += 2 * .pi }
    return Int((angle / (2 * .pi / CGFloat(count))).rounded()) % count
}

// MARK: - UI

final class WheelModel: ObservableObject {
    @Published var tools: [Tool] = []
    @Published var icons: [NSImage] = []
    @Published var running: [Bool] = []  // per tool: its app is open right now
    @Published var hovered: Int?
    @Published var pressed: Int?
    @Published var gearHovered = false
    @Published var open = false
    /// The knob follows the cursor in detents of `detent` radians: clockwise from 12 o'clock, unwrapped so it
    /// always turns the short way round.
    @Published var knobAngle: Double = 0
    /// Settings shows the wheel in edit mode: static, with its controls drawn round the outside.
    let editing: Bool
    @Published var trashHovered: Int?
    /// Each tool's angle on the wheel, by tool id, unwrapped so a reorder glides the short way round.
    @Published var angles: [String: Double] = [:]

    init(editing: Bool = false) { self.editing = editing }

    func load(_ tools: [Tool]) {
        hovered = nil
        trashHovered = nil
        self.tools = tools
        icons = tools.map(\.icon)
        var next: [String: Double] = [:]
        for (i, tool) in tools.enumerated() {
            let slot = Double(i) * 2 * .pi / Double(tools.count)
            next[tool.id] = angles[tool.id].map { $0 + remainder(slot - $0, 2 * .pi) } ?? slot
        }
        angles = next
        // Compare resolved paths: e.g. /Applications/Safari.app is a symlink to the real bundle.
        let open = Set(NSWorkspace.shared.runningApplications.compactMap { $0.bundleURL?.resolvingSymlinksInPath().path })
        running = tools.map { tool in
            tool.expandedPath.map { open.contains(URL(fileURLWithPath: $0).resolvingSymlinksInPath().path) } ?? false
        }
    }

    /// Turns the knob to face slice `i`, the short way round.
    func point(at i: Int) {
        knobAngle += remainder(Double(i) * 2 * .pi / Double(max(tools.count, 1)) - knobAngle, 2 * .pi)
    }

    /// Steps the knob to the detent nearest the cursor; true when it clicked over to a new one.
    func turnKnob(dx: CGFloat, dy: CGFloat) -> Bool {
        let toward = knobAngle + remainder(atan2(dx, dy) - knobAngle, 2 * .pi)  // shortest way round
        let snapped = (toward / detent).rounded() * detent
        guard abs(snapped - knobAngle) > detent / 2 else { return false }
        knobAngle = snapped
        return true
    }
}

let detent = 2 * Double.pi / 36  // 10° per click

let wheelSize: CGFloat = 400     // panel size; extra room so cast shadows aren't clipped
let outerRadius: CGFloat = 172
let centerRadius: CGFloat = 56
let gap: CGFloat = 2             // constant width between neighbouring tools
let knobGap: CGFloat = 1.5       // between the knob and the tools' inner edge
let backdropInset: CGFloat = 8   // frosted backdrop stops this far inside the rim: it fills the gaps but never shows past the edge
let corner: CGFloat = 6          // rounding on wedge corners
let dialRim: CGFloat = 5         // the dial's outer ring, which the light fills
let gearOffset: CGFloat = 26     // settings icon sits this far below the wheel's centre
let gearHitRadius: CGFloat = 14
let runningDotRadius: CGFloat = 82  // "open" dot sits between a tool's icon and the knob
let accent = Color(red: 1, green: 60.0 / 255, blue: 0)  // #FF3C00

let plastic = LinearGradient(colors: [.white, Color(white: 0.935)], startPoint: .top, endPoint: .bottom)

/// Points along a circle of radius `r` around `c`, from angle `from` to `to` (radians, clockwise from 12 o'clock).
func arcPoints(_ c: CGPoint, _ r: CGFloat, _ from: Double, _ to: Double) -> [CGPoint] {
    (0...32).map { i in
        let a = from + (to - from) * Double(i) / 32
        return CGPoint(x: c.x + r * sin(a), y: c.y - r * cos(a))
    }
}

/// Ring segment between angles `start`…`end` (radians, clockwise from 12 o'clock), drawn in the full wheel frame.
/// Edges run parallel to the slice's radial lines, so the gap between neighbours is the same width everywhere.
struct Wedge: Shape {
    let start: Double, end: Double

    func path(in rect: CGRect) -> Path {
        // Geometry is shrunk by `corner`; the same-width round-joined stroke in `body` grows it back with rounded corners.
        let inner = centerRadius + knobGap + corner, outer = outerRadius - corner, half = gap / 2 + corner
        guard end - start > 2 * asin(half / inner) else { return Path() }  // opening or closing: too thin to draw
        let c = CGPoint(x: rect.midX, y: rect.midY)
        var p = Path()
        p.addLines(arcPoints(c, outer, start + asin(half / outer), end - asin(half / outer))
                 + arcPoints(c, inner, end - asin(half / inner), start + asin(half / inner)))
        p.closeSubpath()
        return p
    }
}

/// One slice of plastic between `start` and `end`. Animatable, so slices glide and resize when tools are added,
/// removed or reordered; it also reads how far it's opened (see `Opening`), so a new slice opens in its gap and
/// a removed one closes where it was.
struct Slice: View, Animatable {
    var start: Double, end: Double
    var color: Color? = nil
    @Environment(\.sliceOpening) private var opening

    var animatableData: AnimatablePair<Double, Double> {
        get { AnimatablePair(start, end) }
        set { start = newValue.first; end = newValue.second }
    }

    var body: some View {
        let mid = (start + end) / 2, half = (end - start) / 2 * opening
        let shape = Wedge(start: mid - half, end: mid + half)
        let piece = ZStack {
            shape.fill(plastic)
            shape.stroke(plastic, style: StrokeStyle(lineWidth: corner * 2, lineJoin: .round))
        }
        ZStack {
            piece
            DotTexture().mask(piece)
            if let color {
                // The slice's own shape, kept only along the outer edge, so the band follows its rounded corners.
                ZStack {
                    shape.fill(color)
                    shape.stroke(color, style: StrokeStyle(lineWidth: corner * 2, lineJoin: .round))
                }
                .mask(Circle().strokeBorder(lineWidth: bandWidth).frame(width: outerRadius * 2, height: outerRadius * 2))
            }
        }
    }
}

private struct SliceOpeningKey: EnvironmentKey { static let defaultValue = 1.0 }
extension EnvironmentValues {
    var sliceOpening: Double {
        get { self[SliceOpeningKey.self] }
        set { self[SliceOpeningKey.self] = newValue }
    }
}

/// Transition that opens a slice from nothing (or closes it), by handing `Slice` its opening amount each frame.
struct Opening: ViewModifier, Animatable {
    var amount: Double
    var animatableData: Double {
        get { amount }
        set { amount = newValue }
    }

    func body(content: Content) -> some View { content.environment(\.sliceOpening, amount) }
}

extension AnyTransition {
    /// A slice opening in its gap; closing where it was, fading as it goes. Same timings as Windows.
    static var slice: AnyTransition {
        let open = AnyTransition.modifier(active: Opening(amount: 0), identity: Opening(amount: 1))
        return .asymmetric(insertion: open.combined(with: .opacity.animation(.linear(duration: 0.06))),
                           removal: open.combined(with: .opacity.animation(.easeIn(duration: 0.35))))
    }

    /// Icons and pills: grow and fade in around where they sit (`anchor`), not round the wheel's centre; shrink and
    /// fade out with `leaving`. Same timings as Windows.
    static func popping(at anchor: UnitPoint, leaving: Animation) -> AnyTransition {
        let grow = AnyTransition.scale(scale: 0.6, anchor: anchor)
        return .asymmetric(insertion: grow.animation(.spring(duration: 0.35, bounce: 0.3))
                                .combined(with: .opacity.animation(.linear(duration: 0.25))),
                           removal: grow.combined(with: .opacity).animation(leaving))
    }
}

/// Puts a leaving icon under the one sliding into its place.
struct ZLayer: ViewModifier {
    let z: Double
    func body(content: Content) -> some View { content.zIndex(z) }
}

/// The centre knob: a machined white dial, an outer ring around a slightly smaller face.
struct Dial: View {
    let angle: Double  // turns the scale and pointer, so the whole knob reads as rotating
    let lit: Bool      // pointer shows only while a tool is hovered

    var body: some View {
        let shade = Color(red: 0.2, green: 0.25, blue: 0.28)  // cool grey, like light falling off on plastic
        ZStack {
            Circle().fill(LinearGradient(colors: [.white, Color(white: 0.88)], startPoint: .top, endPoint: .bottom))
            Circle()
                .fill(LinearGradient(colors: [Color(white: 0.995), Color(white: 0.93)], startPoint: .top, endPoint: .bottom))
                .overlay(Circle().strokeBorder(
                    LinearGradient(colors: [Color(white: 0.82), .white], startPoint: .top, endPoint: .bottom), lineWidth: 0.75))
                .padding(dialRim)
            ZStack {
                TickScale()
                // Pointer: a short solid line across the rim, with a soft glow when active.
                Capsule()
                    .fill(accent)
                    .frame(width: 2.5, height: 9)
                    .shadow(color: accent.opacity(0.6), radius: 2.5)
                    .opacity(lit ? 1 : 0)
                    .offset(y: -(centerRadius - 6))
            }
            .rotationEffect(.radians(angle))
            .animation(.easeOut(duration: 0.15), value: lit)
        }
        .frame(width: centerRadius * 2, height: centerRadius * 2)
        .compositingGroup()  // one silhouette casts the shadow, not the face onto the ring
        // Every offset is at least its blur radius, so nothing spills above the knob: light comes from the top.
        .shadow(color: shade.opacity(0.35), radius: 2, y: 2)      // tight contact under the edge
        .shadow(color: shade.opacity(0.45), radius: 4, y: 4)      // dense, short shadow right under the knob
        .shadow(color: shade.opacity(0.38), radius: 12, y: 12)    // soft drop, straight down
    }
}

/// Places a view `radius` out from the centre at `angle` (clockwise from 12 o'clock). Animating the angle moves it
/// round the circle, not across it.
struct Polar: GeometryEffect {
    var angle: Double
    var radius: CGFloat

    var animatableData: AnimatablePair<Double, CGFloat> {
        get { AnimatablePair(angle, radius) }
        set { angle = newValue.first; radius = newValue.second }
    }

    func effectValue(size: CGSize) -> ProjectionTransform {
        ProjectionTransform(CGAffineTransform(translationX: sin(angle) * radius, y: -cos(angle) * radius))
    }
}

/// Frosted glass that blurs whatever is on screen behind the panel.
/// (SwiftUI materials only blur content inside the window, so this drops to NSVisualEffectView.)
struct BackdropBlur: NSViewRepresentable {
    let diameter: CGFloat

    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = .popover
        v.blendingMode = .behindWindow
        v.state = .active                         // stay frosted even though this app never becomes active
        v.appearance = NSAppearance(named: .aqua) // light glass to match the plastic, even in dark mode
        v.maskImage = NSImage(size: NSSize(width: diameter, height: diameter), flipped: false) {
            NSBezierPath(ovalIn: $0).fill()
            return true
        }
        return v
    }

    func updateNSView(_ v: NSVisualEffectView, context: Context) {}
}

/// Fine scale engraved round the edge of the knob face: a tick every 5°, a longer one every 30°.
struct TickScale: View {
    var body: some View {
        Canvas { ctx, size in
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            let r0 = centerRadius - dialRim - 3
            for i in 0..<72 {
                let a = Double(i) * 2 * .pi / 72
                let major = i % 6 == 0
                let r1 = r0 - (major ? 5 : 3)
                var tick = Path()
                tick.move(to: CGPoint(x: c.x + r0 * sin(a), y: c.y - r0 * cos(a)))
                tick.addLine(to: CGPoint(x: c.x + r1 * sin(a), y: c.y - r1 * cos(a)))
                ctx.stroke(tick, with: .color(.black.opacity(major ? 0.26 : 0.16)), lineWidth: major ? 1.0 : 0.7)
            }
        }
    }
}

/// Faint dot grid, like a speaker grille pressed into the plastic.
struct DotTexture: View {
    var color = Color.black.opacity(0.06)
    var step: CGFloat = 7

    var body: some View {
        Canvas { ctx, size in
            for x in stride(from: step / 2, to: size.width, by: step) {
                for y in stride(from: step / 2, to: size.height, by: step) {
                    ctx.fill(Path(ellipseIn: CGRect(x: x - 0.7, y: y - 0.7, width: 1.4, height: 1.4)),
                             with: .color(color))
                }
            }
        }
    }
}

struct WheelView: View {
    @ObservedObject var model: WheelModel
    var backdrop = true  // off for --snapshot (ImageRenderer can't draw NSVisualEffectView) and in Settings

    var step: Double { 2 * .pi / Double(max(model.tools.count, 1)) }

    func wedge(_ i: Int) -> some View {
        let hovered = model.hovered == i, pressed = model.pressed == i
        let mid = model.angles[model.tools[i].id] ?? Double(i) * step
        return Slice(start: mid - step / 2, end: mid + step / 2, color: model.tools[i].color.flatMap { Color(hex: $0) })
        .brightness(pressed ? -0.04 : hovered ? 0.02 : 0)
        .compositingGroup()
        .shadow(color: .black.opacity(hovered && !pressed ? 0.18 : 0), radius: 8, y: 5)
        .offset(x: sin(mid) * lift(i), y: -cos(mid) * lift(i))
        .zIndex(hovered ? 1 : 0)
        .transition(.slice)
    }

    /// Hovered slices nudge outward along their slice; pressed ones sink.
    func lift(_ i: Int) -> CGFloat { model.pressed == i ? -1 : model.hovered == i ? 3 : 0 }

    /// The app sitting in slice `i`: its icon and "open" dot, placed by angle so a reorder glides it round.
    func face(_ i: Int) -> some View {
        let icon = model.icons[i]
        let angle = model.angles[model.tools[i].id] ?? Double(i) * step
        let iconRadius = (centerRadius + outerRadius) / 2 + 4
        return ZStack {
            Image(nsImage: icon)
                .resizable()
                .interpolation(.high)
                .aspectRatio(contentMode: .fit)
                .foregroundStyle(Color(white: 0.28))
                .frame(width: icon.isTemplate ? 26 : 44, height: icon.isTemplate ? 26 : 44)
                .overlay {
                    // Hairline round the app icon's tile. macOS icons fill ~80% of their canvas with ~22.5% corners.
                    if !icon.isTemplate {
                        RoundedRectangle(cornerRadius: 44 * 0.805 * 0.225, style: .continuous)
                            .stroke(.black.opacity(0.09), lineWidth: 0.5)
                            .frame(width: 44 * 0.805, height: 44 * 0.805)
                    }
                }
                .modifier(Polar(angle: angle, radius: iconRadius + lift(i)))
            if model.running.indices.contains(i) && model.running[i] {
                Circle()
                    .fill(Color(white: 0.64))
                    .frame(width: 3.5, height: 3.5)
                    .modifier(Polar(angle: angle, radius: runningDotRadius + lift(i)))
            }
        }
        .frame(width: wheelSize, height: wheelSize)
        .zIndex(2)  // above a lifted slice
        .transition(.popping(at: UnitPoint(x: 0.5 + sin(angle) * iconRadius / wheelSize, y: 0.5 - cos(angle) * iconRadius / wheelSize),
                             leaving: .easeOut(duration: 0.18))  // gone before the neighbour slides over it
            .combined(with: .asymmetric(insertion: .identity, removal: .modifier(active: ZLayer(z: 1.5), identity: ZLayer(z: 2)))))
    }

    var body: some View {
        ZStack {
            ForEach(Array(model.tools.enumerated()), id: \.element.id) { i, _ in wedge(i) }
            ForEach(Array(model.tools.enumerated()), id: \.element.id) { i, _ in face(i) }
            Dial(angle: model.knobAngle, lit: model.hovered != nil)
            let named = model.editing ? model.trashHovered ?? model.hovered : model.hovered  // editing: hovered = being dragged
            Text(model.gearHovered ? "Settings" : named.map { model.tools[$0].name } ?? "")
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Color(white: 0.3))
                .multilineTextAlignment(.center)
                .lineLimit(2)               // wraps once, then ends in "…"
                .truncationMode(.tail)
                .frame(width: centerRadius * 1.4)  // clear of the indicator light's path
                .offset(y: -4)                      // breathing room above the gear
            if model.editing {
                Text("Remove")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(accent)
                    .opacity(model.trashHovered == nil ? 0 : 1)
                    .offset(y: gearOffset)
            } else {
                Image(systemName: "gearshape.fill")
                    .font(.system(size: 13))
                    .foregroundStyle(model.gearHovered ? accent : Color(white: 0.6))
                    .scaleEffect(model.gearHovered ? 1.15 : 1)
                    .offset(y: gearOffset)
            }
        }
        .frame(width: wheelSize, height: wheelSize)
        .compositingGroup()
        .shadow(color: .black.opacity(0.14), radius: 1, y: 1)   // contact
        .shadow(color: .black.opacity(0.16), radius: 7, y: 5)   // cast
        .background {
            let d = (outerRadius - backdropInset) * 2
            // Where the live blur can't be drawn (snapshots, Settings), a plain disc stands in for the frosted glass.
            ZStack {
                if backdrop { BackdropBlur(diameter: d) } else { Circle().fill(Color(white: 0.86)) }
            }
            .frame(width: d, height: d)
        }
        .scaleEffect(model.open ? 1 : 0.88)
        .opacity(model.open ? 1 : 0)
        .animation(.spring(duration: 0.18, bounce: 0.35), value: model.hovered)
        .animation(.spring(duration: 0.1), value: model.pressed)
        .animation(.spring(duration: 0.15), value: model.gearHovered)
        .animation(.easeOut(duration: 0.12), value: model.trashHovered)
    }
}

/// App icon: the knob in its socket on a plastic tile, pointer lit. Drawn at 1/5 size and rendered at 5×
/// (`--icon`), so every stroke and shadow keeps the wheel's proportions. Tile follows Apple's 824/1024 grid.
struct AppIcon: View {
    var body: some View {
        let tile = RoundedRectangle(cornerRadius: 37, style: .continuous)
        ZStack {
            tile.fill(plastic)
            DotTexture()
            Circle()  // socket the knob sits in, like the gap round it on the wheel
                .fill(Color(white: 0.86))
                .frame(width: centerRadius * 2 + 5, height: centerRadius * 2 + 5)
            Dial(angle: .pi / 4, lit: true)
        }
        .frame(width: 164.8, height: 164.8)
        .clipShape(tile)  // the knob's shadow stays on the tile
        .shadow(color: .black.opacity(0.22), radius: 2, y: 2)
        .frame(width: 204.8, height: 204.8)
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate {
    let model = WheelModel()
    let clicker = Clicker()
    var panel: NSPanel!
    var settingsWindow: NSWindow?
    var waitForRelease = false  // after launching a tool, don't reopen until keys are let go
    var ticks = 0
    var badge: Badge!

    func applicationDidFinishLaunching(_ note: Notification) {
        let firstRun = !FileManager.default.fileExists(atPath: configURL.path)
        let config = loadConfig()  // creates tools.json on first run
        Shortcut.current = config.trigger
        Shortcut.button = config.button
        Shortcut.toggle = config.toggle
        Shortcut.releaseToOpen = config.releaseToOpen ?? false
        SideButtons.update()
        badge = Badge()
        OnOff.pressed = { [weak self] in MainActor.assumeIsolated { self?.turnOnOff() } }  // Carbon calls it on the main thread
        // No visible menu bar for an accessory app, but text fields still need these shortcuts.
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        edit.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        let editItem = NSMenuItem()
        editItem.submenu = edit
        NSApp.mainMenu = NSMenu()
        NSApp.mainMenu?.addItem(editItem)

        panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: wheelSize, height: wheelSize),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.level = .popUpMenu
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = NSHostingView(rootView: WheelView(model: model))

        // Ctrl is held, so a click may arrive as a right-click; accept both.
        NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] event in
            guard let self, event.window == self.panel else { return event }
            if self.model.gearHovered {
                self.hide()
                self.waitForRelease = true
                self.openSettings()
            } else if let i = self.model.hovered {
                self.model.pressed = i
                self.model.tools[i].launch()
                self.waitForRelease = true
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.12) { self.hide() }
            }
            return nil
        }

        // ponytail: polls modifier state at 60Hz, which needs no Accessibility/Input Monitoring permission.
        // Switch to a CGEventTap if a non-modifier hotkey (e.g. Cmd+Space) is ever wanted.
        Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }

        // No Dock icon, so show something the first time, or the app looks like it didn't open.
        if firstRun { openSettings() }
    }

    /// Opening the app again (Finder, Spotlight, Launchpad) brings up Settings.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows: Bool) -> Bool {
        openSettings()
        return false
    }

    func tick() {
        ticks += 1
        if ticks % 120 == 0 { SideButtons.update() }  // picks up Accessibility permission granted after the ask
        OnOff.sync()
        Shortcut.settingsOpen = settingsWindow.map { $0.isVisible || $0.isMiniaturized } ?? false
        let held = OnOff.on && !Shortcut.settingsOpen && NSEvent.modifierFlags.intersection(Shortcut.keys) == Shortcut.current
            && (Shortcut.button.map(SideButtons.down) ?? true) && !Shortcut.recording
        if !held { waitForRelease = false }

        if held && !panel.isVisible && !waitForRelease { show() }
        else if !held && panel.isVisible {
            if Shortcut.releaseToOpen {
                if let i = model.hovered { model.tools[i].launch() }
                else if model.gearHovered { openSettings() }
            }
            hide()
        }

        if panel.isVisible {
            let mouse = NSEvent.mouseLocation, f = panel.frame
            let dx = mouse.x - f.midX, dy = mouse.y - f.midY
            let hovered = sliceIndex(dx: dx, dy: dy, count: model.tools.count)
            if hovered != nil, model.turnKnob(dx: dx, dy: dy) { clicker.click() }
            if hovered != model.hovered { model.hovered = hovered }
            let gear = hypot(dx, dy + gearOffset) < gearHitRadius  // AppKit y is up, so "below" is -gearOffset
            if gear != model.gearHovered { model.gearHovered = gear }
        }
    }

    func turnOnOff() {
        OnOff.on.toggle()
        if !OnOff.on && panel.isVisible {
            hide()  // no release-to-open: turning it off shouldn't open anything
            waitForRelease = true
        }
        badge.flash(on: OnOff.on)
    }

    func openSettings() {
        // Fresh view each time so it picks up the current tools.json.
        let window = settingsWindow ?? NSWindow(contentViewController: NSHostingController(rootView: EmptyView()))
        if !window.isVisible {
            let content = NSHostingController(rootView: SettingsView(store: SettingsStore()))
            window.contentViewController = content
            window.title = "Tool Wheel Settings"
            window.titleVisibility = .hidden
            window.titlebarAppearsTransparent = true
            window.styleMask.insert(.fullSizeContentView)
            window.styleMask.remove(.resizable)
            window.appearance = NSAppearance(named: .darkAqua)
            window.backgroundColor = NSColor(white: 0.095, alpha: 1)
            window.isReleasedWhenClosed = false
            // Middle of the screen the cursor is on. (center() sits a third from the top.) The hosting view only
            // resizes the window later, so size it to the SwiftUI content first or the maths uses a 1×0 window.
            window.setContentSize(content.view.fittingSize)
            let mouse = NSEvent.mouseLocation
            if let screen = (NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main)?.visibleFrame {
                window.setFrameOrigin(NSPoint(x: screen.midX - window.frame.width / 2, y: screen.midY - window.frame.height / 2))
            }
        }
        settingsWindow = window
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }

    func show() {
        model.load(loadConfig().wheel)
        model.pressed = nil
        model.gearHovered = false
        model.open = false

        // Center on the cursor, nudged inward if it would spill off-screen.
        let mouse = NSEvent.mouseLocation
        var origin = NSPoint(x: mouse.x - wheelSize / 2, y: mouse.y - wheelSize / 2)
        if let screen = NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) })?.frame {
            origin.x = min(max(origin.x, screen.minX), screen.maxX - wheelSize)
            origin.y = min(max(origin.y, screen.minY), screen.maxY - wheelSize)
        }
        panel.setFrameOrigin(origin)
        panel.orderFrontRegardless()
        clicker.start()
        DispatchQueue.main.async {
            withAnimation(.spring(duration: 0.22, bounce: 0.3)) { self.model.open = true }
        }
    }

    func hide() {
        panel.orderOut(nil)
        clicker.stop()
    }
}

#if DEBUG
assert(sliceIndex(dx: 0, dy: 100, count: 4) == 0)    // top
assert(sliceIndex(dx: 100, dy: 0, count: 4) == 1)    // right
assert(sliceIndex(dx: 0, dy: -100, count: 4) == 2)   // bottom
assert(sliceIndex(dx: -100, dy: 0, count: 4) == 3)   // left
assert(sliceIndex(dx: 5, dy: 5, count: 4) == nil)    // over the knob
assert(sliceIndex(dx: 0, dy: 300, count: 4) == nil)  // outside the wheel
#endif

// `ToolWheel --snapshot out.png` renders the wheel (2nd tool hovered) to a PNG, for checking the look without the hotkey.
if let i = CommandLine.arguments.firstIndex(of: "--snapshot"), i + 1 < CommandLine.arguments.count {
    MainActor.assumeIsolated {
        let model = WheelModel()
        model.load(loadConfig().wheel)
        model.hovered = 1
        model.knobAngle = 2 * .pi / Double(max(model.tools.count, 1))
        model.open = true
        let view = WheelView(model: model, backdrop: false).background(Color(white: 0.93))
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        if let cg = renderer.cgImage {
            try? NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:])?
                .write(to: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
        }
    }
    exit(0)
}

// `ToolWheel --icon out.png` renders the 1024×1024 app icon (see scripts/make-icon.sh).
if let i = CommandLine.arguments.firstIndex(of: "--icon"), i + 1 < CommandLine.arguments.count {
    MainActor.assumeIsolated {
        let renderer = ImageRenderer(content: AppIcon())
        renderer.scale = 5
        if let cg = renderer.cgImage {
            try? NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:])?
                .write(to: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
        }
    }
    exit(0)
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)  // no Dock icon
app.run()
