import AppKit
import SwiftUI

// MARK: - Config

/// One wheel entry. `path` opens an app, file, folder or URL; `shell` runs a zsh command.
struct Tool: Codable {
    let name: String
    var path: String?
    var shell: String?
    var symbol: String?  // optional SF Symbol name to override the icon

    var expandedPath: String? { path.map { ($0 as NSString).expandingTildeInPath } }

    var icon: NSImage {
        if let s = symbol, let img = NSImage(systemSymbolName: s, accessibilityDescription: name) { return img }
        if let p = expandedPath, !p.contains("://") { return NSWorkspace.shared.icon(forFile: p) }
        return NSImage(systemSymbolName: path == nil ? "terminal" : "globe", accessibilityDescription: name)!
    }

    func launch() {
        if let p = expandedPath {
            let url = p.contains("://") ? URL(string: p) : URL(fileURLWithPath: p)
            if let url { NSWorkspace.shared.open(url) }
        } else if let cmd = shell {
            let proc = Process()
            proc.executableURL = URL(fileURLWithPath: "/bin/zsh")
            proc.arguments = ["-lc", cmd]
            try? proc.run()
        }
    }
}

let configURL = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent(".config/toolwheel/tools.json")

let defaultTools = [
    Tool(name: "Finder", path: "/System/Library/CoreServices/Finder.app"),
    Tool(name: "Safari", path: "/Applications/Safari.app"),
    Tool(name: "Terminal", path: "/System/Applications/Utilities/Terminal.app"),
    Tool(name: "Notes", path: "/System/Applications/Notes.app"),
    Tool(name: "Calculator", path: "/System/Applications/Calculator.app"),
    Tool(name: "Settings", path: "/System/Applications/System Settings.app"),
    Tool(name: "Say hi", shell: "say hi", symbol: "waveform"),
]

/// Re-read on every open, so edits to tools.json apply without restarting.
func loadTools() -> [Tool] {
    guard let data = try? Data(contentsOf: configURL) else {
        try? FileManager.default.createDirectory(at: configURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        try? enc.encode(defaultTools).write(to: configURL)
        return defaultTools
    }
    do { return try JSONDecoder().decode([Tool].self, from: data) }
    catch { print("tools.json invalid, using defaults: \(error)"); return defaultTools }
}

// MARK: - Geometry

/// Slice under the cursor. dx/dy in AppKit coords (y up). Slice 0 is at 12 o'clock, going clockwise.
func sliceIndex(dx: CGFloat, dy: CGFloat, count: Int, deadZone: CGFloat = 30) -> Int? {
    guard count > 0, hypot(dx, dy) > deadZone else { return nil }
    var angle = atan2(dx, dy)  // clockwise from north
    if angle < 0 { angle += 2 * .pi }
    return Int((angle / (2 * .pi / CGFloat(count))).rounded()) % count
}

// MARK: - UI

final class WheelModel: ObservableObject {
    @Published var tools: [Tool] = []
    @Published var icons: [NSImage] = []
    @Published var hovered: Int?
}

let wheelSize: CGFloat = 360

struct WheelView: View {
    @ObservedObject var model: WheelModel
    let radius: CGFloat = 115

    var body: some View {
        ZStack {
            Circle().fill(.ultraThinMaterial).frame(width: 310, height: 310)
            ForEach(model.tools.indices, id: \.self) { i in
                let angle = Double(i) * 2 * .pi / Double(model.tools.count)
                let isHovered = model.hovered == i
                Image(nsImage: model.icons[i])
                    .resizable()
                    .frame(width: 48, height: 48)
                    .padding(10)
                    .background(Circle().fill(Color.accentColor.opacity(isHovered ? 0.35 : 0)))
                    .scaleEffect(isHovered ? 1.2 : 1)
                    .offset(x: sin(angle) * radius, y: -cos(angle) * radius)
            }
            Text(model.hovered.map { model.tools[$0].name } ?? "")
                .font(.headline)
        }
        .frame(width: wheelSize, height: wheelSize)
        .animation(.spring(duration: 0.15), value: model.hovered)
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate {
    let trigger: NSEvent.ModifierFlags = [.control, .option, .command]
    let model = WheelModel()
    var panel: NSPanel!
    var waitForRelease = false  // after launching a tool, don't reopen until keys are let go

    func applicationDidFinishLaunching(_ note: Notification) {
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
            if let i = self.model.hovered {
                self.model.tools[i].launch()
                self.hide()
                self.waitForRelease = true
            }
            return nil
        }

        // ponytail: polls modifier state at 60Hz, which needs no Accessibility/Input Monitoring permission.
        // Switch to a CGEventTap if a non-modifier hotkey (e.g. Cmd+Space) is ever wanted.
        Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    func tick() {
        let held = NSEvent.modifierFlags.intersection([.control, .option, .command, .shift]) == trigger
        if !held { waitForRelease = false }

        if held && !panel.isVisible && !waitForRelease { show() }
        else if !held && panel.isVisible { hide() }

        if panel.isVisible {
            let mouse = NSEvent.mouseLocation, f = panel.frame
            let hovered = sliceIndex(dx: mouse.x - f.midX, dy: mouse.y - f.midY, count: model.tools.count)
            if hovered != model.hovered { model.hovered = hovered }
        }
    }

    func show() {
        model.tools = loadTools()
        model.icons = model.tools.map(\.icon)
        model.hovered = nil

        // Center on the cursor, nudged inward if it would spill off-screen.
        let mouse = NSEvent.mouseLocation
        var origin = NSPoint(x: mouse.x - wheelSize / 2, y: mouse.y - wheelSize / 2)
        if let screen = NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) })?.frame {
            origin.x = min(max(origin.x, screen.minX), screen.maxX - wheelSize)
            origin.y = min(max(origin.y, screen.minY), screen.maxY - wheelSize)
        }
        panel.setFrameOrigin(origin)
        panel.orderFrontRegardless()
    }

    func hide() { panel.orderOut(nil) }
}

#if DEBUG
assert(sliceIndex(dx: 0, dy: 100, count: 4) == 0)    // top
assert(sliceIndex(dx: 100, dy: 0, count: 4) == 1)    // right
assert(sliceIndex(dx: 0, dy: -100, count: 4) == 2)   // bottom
assert(sliceIndex(dx: -100, dy: 0, count: 4) == 3)   // left
assert(sliceIndex(dx: 5, dy: 5, count: 4) == nil)    // dead zone
#endif

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)  // no Dock icon
app.run()
