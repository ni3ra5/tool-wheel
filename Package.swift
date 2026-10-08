// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ToolWheel",
    platforms: [.macOS(.v14)],
    targets: [.executableTarget(name: "ToolWheel")]
)
