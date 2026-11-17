// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "DeskKit",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "DeskKit", targets: ["DeskKit"])
    ],
    targets: [
        .target(name: "DeskKitCore"),
        .testTarget(name: "DeskKitCoreTests", dependencies: ["DeskKitCore"]),
        .executableTarget(
            name: "DeskKit",
            dependencies: ["DeskKitCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("ApplicationServices"),
                .linkedFramework("CoreGraphics")
            ]
        )
    ]
)
