import AppKit
import SwiftUI

/// Settings edit tools.json directly; the wheel re-reads it every time it opens.
final class SettingsStore: ObservableObject {
    @Published var config = loadConfig() {
        didSet {
            saveConfig(config)
            preview.load(config.wheel)
            Shortcut.current = config.trigger
            Shortcut.releaseToOpen = config.releaseToOpen ?? false
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
struct WheelEditor: View {
    @ObservedObject var store: SettingsStore

    /// Trash icon under the point, in this view's coordinates (y down).
    func trash(at p: CGPoint) -> Int? {
        let tools = store.preview.tools
        return tools.indices.first { i in
            let a = Double(i) * 2 * .pi / Double(tools.count)
            return hypot(p.x - (wheelSize / 2 + sin(a) * trashRadius), p.y - (wheelSize / 2 - cos(a) * trashRadius)) < trashHitRadius
        }
    }

    var body: some View {
        let preview = store.preview
        WheelView(model: preview, backdrop: false)
            .onContinuousHover { phase in
                guard case .active(let p) = phase else { preview.trashHovered = nil; return }
                let i = trash(at: p)
                if i != preview.trashHovered { preview.trashHovered = i }
            }
            .onTapGesture {
                if let i = preview.trashHovered { store.config.wheel.remove(at: i) }
            }
    }
}

/// Shows the shortcut; click it, then press and release a new combination of two or more modifier keys.
/// Esc or a second click cancels. The arrow restores the default.
struct ShortcutField: View {
    @ObservedObject var store: SettingsStore
    @State private var recording = false
    @State private var held: NSEvent.ModifierFlags = []
    @State private var hint: String?
    @State private var monitor: Any?

    func start() {
        recording = true
        held = []
        hint = nil
        Shortcut.recording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.flagsChanged, .keyDown]) { event in
            if event.type == .keyDown {
                if event.keyCode == 53 { stop() }  // Esc
                return nil                          // don't let keys leak into the search field
            }
            let now = event.modifierFlags.intersection(Shortcut.keys)
            if !now.isEmpty {
                if now.isSuperset(of: held) { held = now }  // remember the most keys held at once
            } else if held.rawValue.nonzeroBitCount >= 2 {
                store.config.shortcut = held.rawValue
                stop()
            } else if !held.isEmpty {
                hint = "Use two or more keys"
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
        Shortcut.recording = false
    }

    var body: some View {
        let isDefault = store.config.trigger == Shortcut.standard
        HStack(spacing: 8) {
            Text(hint ?? (recording ? "Press keys" : "Shortcut"))
                .font(.system(size: 12))
                .foregroundStyle(hint == nil ? .white.opacity(0.6) : accent)
            Button { store.config.shortcut = nil } label: {
                Image(systemName: "arrow.counterclockwise").font(.system(size: 11, weight: .semibold))
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(isDefault ? 0.2 : 0.55))
            .disabled(isDefault)
            .help("Restore \(Shortcut.symbols(Shortcut.standard))")
            Button { recording ? stop() : start() } label: {
                Text(recording ? (held.isEmpty ? "…" : Shortcut.symbols(held)) : Shortcut.symbols(store.config.trigger))
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(recording ? accent : .white.opacity(0.9))
                    .frame(minWidth: 44)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(RoundedRectangle(cornerRadius: 6).fill(.white.opacity(0.06)))
                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(accent.opacity(recording ? 1 : 0), lineWidth: 1))
            }
            .buttonStyle(.plain)
            .help("Click, then hold the keys you want")
        }
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
                LibraryRow(tool: tool, onWheel: store.isOnWheel(tool)) { store.toggle(tool) }
            }
        }
    }

    var body: some View {
        HStack(spacing: 0) {
            ZStack {
                WheelEditor(store: store)
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
