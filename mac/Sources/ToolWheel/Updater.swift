import AppKit
import CryptoKit
import SwiftUI

/// Asks GitHub Releases for a newer mac-v* release (at launch, or from "Check now") and, when the user says so, installs
/// it: downloads the zip, checks it against the release's SHA256SUMS, swaps in the new .app and restarts. Same flow as
/// Windows' Updater, which swaps ToolWheel.exe.
enum Updater {
    static let api = URL(string: "https://api.github.com/repos/ni3ra5/tool-wheel/releases?per_page=30")!
    static let page = URL(string: "https://github.com/ni3ra5/tool-wheel/releases")!

    struct Release {
        let version: [Int]
        let name: String  // "0.1.5"
        let notes: String
        let zip: URL
        let sums: URL?
        let page: URL
    }

    struct Failure: LocalizedError {
        let message: String
        init(_ message: String) { self.message = message }
        var errorDescription: String? { message }
    }

    /// This build's version, from the .app's Info.plist; nil for development builds (swift run, or a .app built without
    /// a version, which is 0.0.0), which never update.
    static let current: [Int]? = {
        guard let text = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String,
              let version = parse(text), version != [0, 0, 0] else { return nil }
        return version
    }()

    static var currentText: String { current.map { $0.map(String.init).joined(separator: ".") } ?? "Development build" }

    static func parse(_ text: String) -> [Int]? {
        let parts = text.split(separator: ".").map { Int($0) }
        return parts.isEmpty || parts.contains(nil) ? nil : parts.compactMap { $0 }
    }

    static func newer(_ a: [Int], than b: [Int]) -> Bool {
        for i in 0..<max(a.count, b.count) {
            let x = i < a.count ? a[i] : 0, y = i < b.count ? b[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    private struct GitHubRelease: Decodable {
        struct Asset: Decodable {
            let name: String
            let browser_download_url: String
        }
        let tag_name: String
        let draft: Bool
        let prerelease: Bool
        let body: String?
        let html_url: String
        let assets: [Asset]
    }

    /// The newest Mac release if it's newer than this build, else nil. Throws when GitHub can't be reached.
    static func check() async throws -> Release? {
        guard let current else { return nil }
        var request = URLRequest(url: api)
        request.setValue("ToolWheel", forHTTPHeaderField: "User-Agent")
        let (data, _) = try await URLSession.shared.data(for: request)
        var newest: Release?
        for r in try JSONDecoder().decode([GitHubRelease].self, from: data) where !r.draft && !r.prerelease && r.tag_name.hasPrefix("mac-v") {
            let name = String(r.tag_name.dropFirst("mac-v".count))
            guard let version = parse(name),
                  let zip = r.assets.first(where: { $0.name.hasSuffix(".zip") }).flatMap({ URL(string: $0.browser_download_url) })
            else { continue }
            if let best = newest, !newer(version, than: best.version) { continue }
            newest = Release(version: version, name: name, notes: cleanNotes(r.body), zip: zip,
                             sums: r.assets.first(where: { $0.name == "SHA256SUMS" }).flatMap { URL(string: $0.browser_download_url) },
                             page: URL(string: r.html_url) ?? page)
        }
        guard let newest, newer(newest.version, than: current) else { return nil }
        return newest
    }

    /// GitHub's generated notes as plain lines: no headings, bold or "Full Changelog" link, bullets as •.
    static func cleanNotes(_ markdown: String?) -> String {
        (markdown ?? "").replacingOccurrences(of: "\r", with: "")
            .split(separator: "\n", omittingEmptySubsequences: false)
            .filter { !$0.hasPrefix("**Full Changelog**") }
            .map { line -> String in
                var l = line.trimmingCharacters(in: .whitespaces)
                while l.hasPrefix("#") { l.removeFirst() }
                l = l.trimmingCharacters(in: .whitespaces)
                if l.hasPrefix("* ") || l.hasPrefix("- ") { l = "• " + l.dropFirst(2) }
                return l.replacingOccurrences(of: "**", with: "")
            }
            .filter { !$0.isEmpty }
            .joined(separator: "\n")
    }

    /// Downloads, verifies and swaps in the new .app, then has it opened a moment after this copy quits (the caller
    /// quits right after).
    static func install(_ release: Release, status: @escaping (String) -> Void) async throws {
        guard let sums = release.sums else { throw Failure("this release has no checksums") }
        let app = Bundle.main.bundleURL
        guard app.pathExtension == "app" else { throw Failure("not running from Tool Wheel.app") }
        status("Downloading…")
        let (zipFile, _) = try await URLSession.shared.download(from: release.zip)
        let (sumsData, _) = try await URLSession.shared.data(from: sums)
        let name = release.zip.lastPathComponent
        let expected = String(decoding: sumsData, as: UTF8.self).split(separator: "\n")
            .map { $0.split(separator: " ", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) } }
            .first(where: { $0.count == 2 && $0[1].trimmingCharacters(in: CharacterSet(charactersIn: "*")) == name })?[0]
        guard let expected else { throw Failure("no checksum for \(name)") }
        let digest = SHA256.hash(data: try Data(contentsOf: zipFile)).map { String(format: "%02x", $0) }.joined()
        guard digest == expected.lowercased() else { throw Failure("the download didn't match its checksum") }

        status("Installing…")
        let unpacked = FileManager.default.temporaryDirectory.appendingPathComponent("ToolWheel-update-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: unpacked, withIntermediateDirectories: true)
        try run("/usr/bin/ditto", "-x", "-k", zipFile.path, unpacked.path)
        let fresh = unpacked.appendingPathComponent("Tool Wheel.app")
        guard FileManager.default.fileExists(atPath: fresh.path) else { throw Failure("the download has no Tool Wheel.app") }
        _ = try FileManager.default.replaceItemAt(app, withItemAt: fresh)
        try run("/bin/sh", "-c", "(sleep 1; /usr/bin/open \"$0\") >/dev/null 2>&1 &", app.path)  // once this copy has quit
    }

    private static func run(_ tool: String, _ arguments: String...) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw Failure("couldn't unpack the download") }
    }
}

/// "Tool Wheel 0.1.5 is available", what's new, then Skip this version / Later / Update now. Same layout, wording and
/// sizes as Windows' UpdateWindow: dark and flat like Settings, 420 wide.
struct UpdateView: View {
    let release: Updater.Release
    let skip: () -> Void
    let close: () -> Void
    @State private var status: String?
    @State private var failed = false

    func pill(_ title: String, fill: Color, text: Color, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(text)
                .padding(.horizontal, 14)
                .padding(.top, 5)
                .padding(.bottom, 6)
                .background(RoundedRectangle(cornerRadius: 6).fill(fill))
        }
        .buttonStyle(.plain)
    }

    func update() {
        status = "Downloading…"
        Task { @MainActor in
            do {
                try await Updater.install(release) { text in Task { @MainActor in status = text } }
                NSApp.terminate(nil)
            } catch {
                status = "Couldn't update: \(error.localizedDescription)"
                failed = true
            }
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("Tool Wheel \(release.name) is available")
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(.white.opacity(0.92))
            Text("You have \(Updater.currentText). Updating takes a few seconds and restarts Tool Wheel.")
                .font(.system(size: 12))
                .foregroundStyle(.white.opacity(0.55))
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 6)
            if !release.notes.isEmpty {
                let notes = Text(release.notes)
                    .font(.system(size: 12))
                    .foregroundStyle(.white.opacity(0.75))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(12)
                Group {
                    if release.notes.split(separator: "\n").count > 10 { ScrollView { notes }.frame(height: 180) } else { notes }
                }
                .background(RoundedRectangle(cornerRadius: 8).fill(.white.opacity(0.04)))
                .padding(.top, 16)
            }
            HStack(spacing: 8) {
                if let status {
                    Text(status)
                        .font(.system(size: 12))
                        .foregroundStyle(failed ? accent : .white.opacity(0.6))
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    pill("Skip this version", fill: .clear, text: .white.opacity(0.55)) { skip(); close() }
                        .padding(.leading, -14)  // text flush with the title
                }
                Spacer(minLength: 8)
                if failed {
                    pill("Open download page", fill: .white.opacity(0.08), text: .white.opacity(0.9)) {
                        NSWorkspace.shared.open(release.page)
                        close()
                    }
                    pill("Close", fill: .white.opacity(0.08), text: .white.opacity(0.9)) { close() }
                } else if status == nil {
                    pill("Later", fill: .white.opacity(0.08), text: .white.opacity(0.9)) { close() }
                    pill("Update now", fill: accent, text: .white) { update() }
                }
            }
            .padding(.top, 20)
        }
        .padding(.horizontal, 24)
        .padding(.vertical, 22)
        .frame(width: 420)
        .background(Color(white: 0.095))
        .preferredColorScheme(.dark)
    }
}

/// Under the app list in Settings: the version (swapped for the result of "Check now" for a few seconds) with "Check
/// now" on the right, then whether to check at launch. Same as Windows' footer.
struct UpdatesFooter: View {
    @ObservedObject var store: SettingsStore
    @State private var result: String?
    @State private var hovering = false

    func check() {
        result = "Checking…"
        Task { @MainActor in
            result = await delegate.checkNow()
            if result != nil {
                try? await Task.sleep(nanoseconds: 4_000_000_000)
                result = nil
            }
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Text(result ?? (Updater.current == nil ? "Development build" : "Version \(Updater.currentText)"))
                    .foregroundStyle(.white.opacity(result == nil ? 0.35 : 0.6))
                Spacer(minLength: 8)
                Button("Check now") { check() }
                    .buttonStyle(.plain)
                    .foregroundStyle(hovering ? accent : .white.opacity(0.6))
                    .onHover { hovering = $0 }
            }
            HStack(spacing: 8) {
                Toggle("Check for updates at launch", isOn: Binding(
                    get: { store.config.checkForUpdates != false },
                    set: { store.config.checkForUpdates = $0 ? nil : false }
                ))
                .toggleStyle(.switch)
                .controlSize(.small)
                .labelsHidden()
                Text("Check for updates at launch").foregroundStyle(.white.opacity(0.6))
            }
        }
        .font(.system(size: 12))
        .padding(.top, 12)
        .overlay(alignment: .top) { Rectangle().fill(.white.opacity(0.06)).frame(height: 1) }
        .padding(.top, 12)
    }
}
