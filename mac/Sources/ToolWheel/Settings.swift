import AppKit
import SwiftUI

/// Settings edit tools.json directly; the wheel re-reads it every time it opens.
final class SettingsStore: ObservableObject {
    @Published var config = loadConfig() {
        didSet {
            saveConfig(config)
            preview.load(config.wheel)
            Shortcut.current = config.trigger
            Shortcut.button = config.button
            Shortcut.toggle = config.toggle
            Shortcut.releaseToOpen = config.releaseToOpen ?? false
            SideButtons.update()
        }
    }
    /// The wheel shown on the left, in edit mode.
    let preview = WheelModel(editing: true)

    init() {
        preview.load(config.wheel)
        preview.open = true
    }

    func isOnWheel(_ tool: Tool) -> Bool { config.wheel.contains { $0.id == tool.id } }

    func toggle(_ tool: Tool) {
        if isOnWheel(tool) { config.wheel.removeAll { $0.id == tool.id } }
        else { config.wheel.append(tool) }
    }
}

/// Apps in the usual install folders (one level of subfolders, e.g. /Applications/Utilities).
func installedApps() -> [Tool] {
    let fm = FileManager.default
    var apps = [Tool(name: "Finder", path: "/System/Library/CoreServices/Finder.app")]
    for dir in ["/Applications", "/System/Applications", "~/Applications"] {
        let url = URL(fileURLWithPath: (dir as NSString).expandingTildeInPath)
        // Not .skipsHiddenFiles: Safari's entry in /Applications is a symlink flagged hidden.
        guard let e = fm.enumerator(at: url, includingPropertiesForKeys: nil,
                                    options: [.skipsPackageDescendants]) else { continue }
        for case let app as URL in e {
            if e.level >= 2 { e.skipDescendants() }
            guard app.pathExtension == "app" else { continue }
            var name = fm.displayName(atPath: app.path)
            if name.hasSuffix(".app") { name = String(name.dropLast(4)) }
            apps.append(Tool(name: name, path: app.path))
        }
    }
    return apps.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
}

// ponytail: a LaunchAgent pointing at this binary, because a bare SwiftPM executable can't use SMAppService.
// Switch to SMAppService.mainApp once this ships as a signed .app bundle (it also shows in Login Items then).
enum LaunchAtLogin {
    static let label = "com.ni3ra5.toolwheel"
    static let plist = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/LaunchAgents/\(label).plist")

    static var isEnabled: Bool { FileManager.default.fileExists(atPath: plist.path) }

    static func set(_ on: Bool) {
        guard on else { try? FileManager.default.removeItem(at: plist); return }
        let agent: [String: Any] = [
            "Label": label,
            "ProgramArguments": [Bundle.main.executableURL!.resolvingSymlinksInPath().path],
            "RunAtLoad": true,
            "ProcessType": "Interactive",
        ]
        try? FileManager.default.createDirectory(at: plist.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? PropertyListSerialization.data(fromPropertyList: agent, format: .xml, options: 0).write(to: plist)
    }
}

// MARK: - Views

/// One tool in the library. Click to put it on the wheel, or take it off; a checkmark shows it's there.
struct LibraryRow: View {
    let tool: Tool
    let onWheel: Bool
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        HStack(spacing: 10) {
            let icon = tool.icon
            Image(nsImage: icon)
                .resizable()
                .aspectRatio(contentMode: .fit)
                .foregroundStyle(.white.opacity(0.75))
                .frame(width: icon.isTemplate ? 16 : 24, height: icon.isTemplate ? 16 : 24)
                .frame(width: 24)
            Text(tool.name)
                .font(.system(size: 13))
                .foregroundStyle(.white.opacity(0.88))
                .lineLimit(1)
            Spacer()
            Image(systemName: onWheel ? "checkmark" : "plus")
                .font(.system(size: 11, weight: .bold))
                .foregroundStyle(onWheel ? accent : .white.opacity(0.45))
                .opacity(onWheel || hovering ? 1 : 0)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 5)
        .background(RoundedRectangle(cornerRadius: 7).fill(.white.opacity(hovering ? 0.06 : 0)))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onTapGesture(perform: action)
    }
}

/// The wheel as it looks, static. Each tool has a trash icon at its outer edge; click it to remove the tool.
/// Six-dot drag handle.
struct Grip: View {
    var body: some View {
        VStack(spacing: 2.5) {
            ForEach(0..<3, id: \.self) { _ in
                HStack(spacing: 2.5) {
                    Circle().frame(width: 3, height: 3)
                    Circle().frame(width: 3, height: 3)
                }
            }
        }
    }
}

extension View {
    /// Open hand over a drag handle, closed while dragging.
    @ViewBuilder func grabCursor(active: Bool) -> some View {
        if #available(macOS 15, *) {
            pointerStyle(active ? .grabActive : .grabIdle)
        } else {
            onHover { $0 ? NSCursor.openHand.push() : NSCursor.pop() }
        }
    }
}

let editorSize = wheelSize + 124       // room for the controls ring outside the wheel
let pillSize = CGSize(width: 80, height: 26)
let holdToDelete = 0.7  // seconds the trash must be held

/// Pills are wider than tall, so at 3 and 9 o'clock they reach further toward the wheel; push those out
/// so the clearance from the rim is the same all round.
func controlsRadius(_ angle: Double) -> CGFloat {
    outerRadius + 10 + pillSize.height / 2 + abs(sin(angle)) * (pillSize.width - pillSize.height) / 2
}

/// Swatches for a slot's colour band: none, or one of the presets.
struct SwatchPicker: View {
    let selected: String?
    let pick: (String?) -> Void

    var body: some View {
        let swatches: [(name: String, hex: String?)] = [("None", nil)] + slotColors.map { ($0.name, Optional($0.hex)) }
        LazyVGrid(columns: Array(repeating: GridItem(.fixed(22), spacing: 8), count: 3), spacing: 8) {
            ForEach(swatches, id: \.name) { swatch in
                Button { pick(swatch.hex) } label: {
                    ZStack {
                        if let hex = swatch.hex, let color = Color(hex: hex) {
                            Circle().fill(color)
                        } else {
                            Circle().strokeBorder(.white.opacity(0.4), lineWidth: 1)
                            Rectangle().fill(.white.opacity(0.4)).frame(width: 1, height: 16).rotationEffect(.degrees(45))
                        }
                    }
                    .frame(width: 18, height: 18)
                    .padding(2)
                    .overlay(Circle().strokeBorder(.white.opacity(swatch.hex == selected ? 0.9 : 0), lineWidth: 1.5))
                    .contentShape(Circle())  // "None" is only an outline, which otherwise takes clicks on its line alone
                }
                .buttonStyle(.plain)
                .help(swatch.name)
            }
        }
        .padding(12)
    }
}

/// The wheel as it looks, static, with a control pill outside each tool's edge: colour dot, trash (hold) to remove it,
/// six-dot grip to drag it round to a new position. Tools glide to their new places.
struct WheelEditor: View {
    @ObservedObject var store: SettingsStore
    @State private var dragging: String?  // id of the tool being dragged
    @State private var coloring: String?  // id of the tool whose colour picker is open
    @State private var holding: String?   // id of the tool whose trash is (or was last) held
    @State private var holdProgress: CGFloat = 0

    func setColor(_ tool: Tool, _ hex: String?) {
        if let i = store.config.wheel.firstIndex(where: { $0.id == tool.id }) { store.config.wheel[i].color = hex }  // saves
        coloring = nil
    }

    /// Slice in the direction of the point (editor coordinates); anywhere outside the knob counts.
    func slice(at p: CGPoint) -> Int? {
        sliceIndex(dx: p.x - editorSize / 2, dy: editorSize / 2 - p.y, count: store.preview.tools.count, outer: .infinity)
    }

    func reorder(_ tool: Tool) -> some Gesture {
        DragGesture(minimumDistance: 1, coordinateSpace: .named("editor"))
            .onChanged { drag in
                let preview = store.preview
                guard let from = preview.tools.firstIndex(where: { $0.id == tool.id }) else { return }
                if dragging == nil {
                    dragging = tool.id
                    preview.trashHovered = nil
                    preview.hovered = from
                }
                guard let to = slice(at: drag.location), to != from else { return }
                var tools = preview.tools
                tools.move(fromOffsets: [from], toOffset: to > from ? to + 1 : to)
                withAnimation(.spring(duration: 0.35, bounce: 0.15)) {
                    preview.load(tools)
                    preview.hovered = to
                    preview.point(at: to)
                }
            }
            .onEnded { _ in
                if store.preview.tools != store.config.wheel { store.config.wheel = store.preview.tools }  // saves
                dragging = nil
                store.preview.hovered = nil
            }
    }

    func controls(_ i: Int, _ tool: Tool) -> some View {
        let preview = store.preview
        let active = dragging == tool.id || (dragging == nil && preview.hovered == i)
        let angle = preview.angles[tool.id] ?? 0
        let color = tool.color.flatMap { Color(hex: $0) }
        return HStack(spacing: 9) {
            Button { coloring = tool.id } label: {
                ZStack {
                    if let color { Circle().fill(color) }
                    else { Circle().strokeBorder(.white.opacity(0.45), style: StrokeStyle(lineWidth: 1, dash: [2, 1.5])) }
                }
                .frame(width: 11, height: 11)
                .frame(width: 18, height: 18)
                .contentShape(Circle())  // a dashed outline alone only takes clicks on its line
            }
            .buttonStyle(.plain)
            .help("Colour for \(tool.name)")
            .popover(isPresented: Binding(get: { coloring == tool.id }, set: { if !$0 { coloring = nil } }), arrowEdge: .bottom) {
                SwatchPicker(selected: tool.color) { setColor(tool, $0) }
            }

            // Hold to delete: the pill fills left to right; letting go early drains it and nothing happens.
            Image(systemName: "trash")
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(holding == tool.id && holdProgress > 0 ? .white : preview.trashHovered == i ? accent : .white.opacity(0.45))
                .frame(width: 18, height: 18)
                .contentShape(Rectangle())
                .onHover { inside in
                    guard dragging == nil else { return }
                    preview.trashHovered = inside ? i : nil
                }
                .onLongPressGesture(minimumDuration: holdToDelete, maximumDistance: 30) {
                    NSHapticFeedbackManager.defaultPerformer.perform(.generic, performanceTime: .now)
                    withAnimation(.spring(duration: 0.35, bounce: 0.15)) { store.config.wheel.removeAll { $0.id == tool.id } }
                } onPressingChanged: { pressing in
                    if pressing {
                        holding = tool.id
                        withAnimation(.linear(duration: holdToDelete)) { holdProgress = 1 }
                    } else {
                        withAnimation(.easeOut(duration: 0.2)) { holdProgress = 0 }
                    }
                }
                .help("Hold to remove \(tool.name)")

            Grip()
                .foregroundStyle(.white.opacity(active ? 0.85 : 0.45))
                .padding(4)
                .contentShape(Rectangle())
                .grabCursor(active: dragging == tool.id)
                .onHover { inside in
                    guard dragging == nil else { return }
                    preview.hovered = inside ? i : nil
                    if inside { preview.point(at: i) }
                }
                .gesture(reorder(tool))
        }
        .frame(width: pillSize.width, height: pillSize.height)
        .background {
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(active || preview.trashHovered == i ? 0.09 : 0.04))
                Rectangle().fill(accent.opacity(0.6)).frame(width: pillSize.width * (holding == tool.id ? holdProgress : 0))
            }
            .clipShape(Capsule())
        }
        .modifier(Polar(angle: angle, radius: controlsRadius(angle)))
        .transition(.popping(at: UnitPoint(x: 0.5 + sin(angle) * controlsRadius(angle) / pillSize.width,
                                           y: 0.5 - cos(angle) * controlsRadius(angle) / pillSize.height),
                             leaving: .easeIn(duration: 0.35)))
    }

    var body: some View {
        let preview = store.preview
        ZStack {
            WheelView(model: preview, backdrop: false)
                .allowsHitTesting(false)  // the wheel itself is just a picture; the controls do the work
            ForEach(Array(preview.tools.enumerated()), id: \.element.id) { i, tool in controls(i, tool) }
        }
        .frame(width: editorSize, height: editorSize)
        .coordinateSpace(name: "editor")
    }
}

extension Notification.Name {
    /// Starting one shortcut recorder stops the other.
    static let stopRecording = Notification.Name("ToolWheel.stopRecording")
}

/// Shows the shortcut; click it, then press and release a new combination of two or more modifier keys, or a mouse
/// side button on its own or with keys. Esc or a second click cancels. The arrow restores the default.
struct ShortcutField: View {
    @ObservedObject var store: SettingsStore
    @State private var recording = false
    @State private var held: NSEvent.ModifierFlags = []
    @State private var heldButton: Int?
    @State private var buttonDown = false
    @State private var hint: String?
    @State private var monitor: Any?

    func start() {
        NotificationCenter.default.post(name: .stopRecording, object: nil)
        recording = true
        held = []
        heldButton = nil
        buttonDown = false
        hint = nil
        Shortcut.recording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.flagsChanged, .keyDown, .otherMouseDown, .otherMouseUp]) { event in
            switch event.type {
            case .keyDown:
                if event.keyCode == 53 { stop() }  // Esc
                return nil                          // don't let keys leak into the search field
            case .otherMouseDown where event.buttonNumber == 3 || event.buttonNumber == 4:  // buttons 4 and 5
                if heldButton == nil { heldButton = event.buttonNumber + 1 }
                buttonDown = true
            case .otherMouseUp where event.buttonNumber + 1 == heldButton:
                buttonDown = false
            default:
                break
            }
            let now = event.modifierFlags.intersection(Shortcut.keys)
            if !now.isEmpty || buttonDown {
                if now.isSuperset(of: held) { held = now }  // remember the most keys held at once
            } else if heldButton != nil || held.rawValue.nonzeroBitCount >= 2 {
                var config = store.config
                config.shortcut = held.rawValue
                config.mouseButton = heldButton
                store.config = config
                stop()
                if config.button != nil { SideButtons.update(prompt: true) }
            } else if !held.isEmpty {
                hint = "Use two keys or a side button"
                held = []
            }
            return nil
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        recording = false
        held = []
        heldButton = nil
        buttonDown = false
        Shortcut.recording = false
    }

    var body: some View {
        let isDefault = store.config.isDefaultTrigger
        HStack(spacing: 8) {
            Text(hint ?? (recording ? "Press keys or a side button" : "Shortcut"))
                .font(.system(size: 12))
                .foregroundStyle(hint == nil ? .white.opacity(0.6) : accent)
            Button {
                var config = store.config
                config.shortcut = nil
                config.mouseButton = nil
                store.config = config
            } label: {
                Image(systemName: "arrow.counterclockwise").font(.system(size: 11, weight: .semibold))
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(isDefault ? 0.2 : 0.55))
            .disabled(isDefault)
            .help("Restore \(Shortcut.symbols(Shortcut.standard))")
            Button { recording ? stop() : start() } label: {
                Text(recording ? (held.isEmpty && heldButton == nil ? "…" : Shortcut.describe(held, heldButton))
                               : Shortcut.describe(store.config.trigger, store.config.button))
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(recording ? accent : .white.opacity(0.9))
                    .frame(minWidth: 44)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(RoundedRectangle(cornerRadius: 6).fill(.white.opacity(0.06)))
                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(accent.opacity(recording ? 1 : 0), lineWidth: 1))
            }
            .buttonStyle(.plain)
            .help("Click, then hold the keys or mouse side button you want")
        }
        .onReceive(NotificationCenter.default.publisher(for: .stopRecording)) { _ in if recording { stop() } }
        .onDisappear { stop() }
    }
}

/// The on/off shortcut: click it, then press a key while holding at least one modifier (⌃⌥P). Esc or a second click
/// cancels; the cross clears it, leaving no on/off shortcut.
struct ToggleField: View {
    @ObservedObject var store: SettingsStore
    @State private var recording = false
    @State private var held: NSEvent.ModifierFlags = []
    @State private var hint: String?
    @State private var monitor: Any?

    func start() {
        NotificationCenter.default.post(name: .stopRecording, object: nil)
        recording = true
        held = []
        hint = nil
        Shortcut.recording = true  // OnOff unregisters the hotkey meanwhile, so pressing it lands here
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.flagsChanged, .keyDown]) { event in
            held = event.modifierFlags.intersection(Shortcut.keys)
            guard event.type == .keyDown else { return nil }
            if event.keyCode == 53 {  // Esc
                stop()
            } else if held.isEmpty {
                hint = "Add ⌃, ⌥, ⇧ or ⌘"
            } else {
                var config = store.config
                config.toggleKey = Int(event.keyCode)
                config.toggleModifiers = held.rawValue
                store.config = config
                stop()
            }
            return nil  // don't let keys leak into the search field
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        recording = false
        held = []
        hint = nil
        Shortcut.recording = false
    }

    var body: some View {
        let none = store.config.toggle == nil
        HStack(spacing: 8) {
            Text(hint ?? (recording ? "Hold keys, press a key" : "Turn wheel on/off"))
                .font(.system(size: 12))
                .foregroundStyle(hint == nil ? .white.opacity(0.6) : accent)
            Button {
                var config = store.config
                config.toggleKey = nil
                config.toggleModifiers = nil
                store.config = config
            } label: {
                Image(systemName: "xmark").font(.system(size: 10, weight: .semibold))
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(none ? 0.2 : 0.55))
            .disabled(none)
            .help("No on/off shortcut")
            Button { recording ? stop() : start() } label: {
                Text(recording ? (held.isEmpty ? "…" : Shortcut.symbols(held) + "…")
                               : store.config.toggle.map { Shortcut.symbols($0.modifiers) + Shortcut.keyName($0.key) } ?? "None")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(recording ? accent : .white.opacity(none ? 0.35 : 0.9))
                    .frame(minWidth: 44)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(RoundedRectangle(cornerRadius: 6).fill(.white.opacity(0.06)))
                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(accent.opacity(recording ? 1 : 0), lineWidth: 1))
            }
            .buttonStyle(.plain)
            .help("Click, then press the keys that turn the wheel off and on")
        }
        .onReceive(NotificationCenter.default.publisher(for: .stopRecording)) { _ in if recording { stop() } }
        .onDisappear { stop() }
    }
}

struct SettingsView: View {
    @ObservedObject var store: SettingsStore
    @State private var query = ""
    @State private var launchAtLogin = LaunchAtLogin.isEnabled
    @State private var installed = installedApps()

    func matches(_ tools: [Tool]) -> [Tool] {
        query.isEmpty ? tools : tools.filter { $0.name.localizedCaseInsensitiveContains(query) }
    }

    func section(_ title: String, _ tools: [Tool]) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(title.uppercased())
                .font(.system(size: 10, weight: .semibold))
                .tracking(0.8)
                .foregroundStyle(.white.opacity(0.35))
                .padding(.horizontal, 10)
                .padding(.top, 10)
                .padding(.bottom, 4)
            ForEach(tools) { tool in
                LibraryRow(tool: tool, onWheel: store.isOnWheel(tool)) {
                    withAnimation(.spring(duration: 0.35, bounce: 0.15)) { store.toggle(tool) }  // slices make room or close up
                }
            }
        }
    }

    var body: some View {
        HStack(spacing: 0) {
            ZStack {
                WheelEditor(store: store)
                    .offset(y: -38)  // clear of the controls along the bottom
                if store.config.wheel.isEmpty {
                    Text("Add tools from the list").font(.system(size: 11)).foregroundStyle(.white.opacity(0.35))
                        .offset(y: outerRadius + 24)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .overlay(alignment: .bottomLeading) {
                HStack(spacing: 8) {
                    Toggle("Open at login", isOn: Binding(
                        get: { launchAtLogin },
                        set: { LaunchAtLogin.set($0); launchAtLogin = LaunchAtLogin.isEnabled }
                    ))
                    .toggleStyle(.switch)
                    .controlSize(.small)
                    .labelsHidden()
                    Text("Open at login")
                        .font(.system(size: 12))
                        .foregroundStyle(.white.opacity(0.6))
                }
                .padding(20)
            }
            .overlay(alignment: .bottomTrailing) {
                VStack(alignment: .trailing, spacing: 12) {
                    ShortcutField(store: store)
                    ToggleField(store: store)
                    HStack(spacing: 8) {
                        Text("Open apps by")
                            .font(.system(size: 12))
                            .foregroundStyle(.white.opacity(0.6))
                        Picker("Open apps by", selection: Binding(
                            get: { store.config.releaseToOpen ?? false },
                            set: { store.config.releaseToOpen = $0 }
                        )) {
                            Text("Click").tag(false)
                            Text("Release").tag(true)
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                        .controlSize(.small)
                        .fixedSize()
                        .help("Release: hover a tool and let go of the shortcut to open it")
                    }
                }
                .padding(20)
            }

            Rectangle().fill(.white.opacity(0.06)).frame(width: 1)

            VStack(alignment: .leading, spacing: 14) {
                Text("Add tools")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.white.opacity(0.9))
                HStack(spacing: 6) {
                    Image(systemName: "magnifyingglass").foregroundStyle(.white.opacity(0.35))
                    TextField("Search apps and tools", text: $query)
                        .textFieldStyle(.plain)
                        .foregroundStyle(.white.opacity(0.9))
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(RoundedRectangle(cornerRadius: 8).fill(.white.opacity(0.06)))

                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        let apps = matches(installed)
                        if !apps.isEmpty { section("Installed apps", apps) }
                        if apps.isEmpty {
                            Text("No matches").font(.system(size: 12)).foregroundStyle(.white.opacity(0.35)).padding(10)
                        }
                    }
                }
                .scrollIndicators(.never)
                .padding(.horizontal, -10)  // rows carry their own inset

                UpdatesFooter(store: store)
                    .padding(.top, -14)  // the stack's spacing; the footer brings its own

            }
            .padding(.horizontal, 24)
            .padding(.vertical, 20)
            .frame(width: 330)
        }
        .frame(width: 860, height: 580)
        .overlay(alignment: .top) { Rectangle().fill(.white.opacity(0.08)).frame(height: 1) }  // under the title bar
        .background {
            ZStack {
                Color(white: 0.095)
                DotTexture(color: .white.opacity(0.07), step: 14)
            }
            .ignoresSafeArea()
        }
        .preferredColorScheme(.dark)
        .tint(accent)
    }
}
