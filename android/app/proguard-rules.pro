# Keep all public API surface.
-keepattributes *Annotation*
-keepattributes SourceFile,LineNumberTable

# MediaCodec is called by name in some Android versions -- keep it.
-keep class android.media.MediaCodec { *; }
-keep class android.media.MediaFormat { *; }

# NSD manager internals.
-keep class android.net.nsd.** { *; }

# JSON classes used for wire protocol.
-keep class org.json.** { *; }
