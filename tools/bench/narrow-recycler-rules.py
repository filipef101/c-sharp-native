import pathlib
import sys

root = pathlib.Path(sys.argv[1])
if not root.is_dir():
    raise SystemExit(f"missing {root}")
needle = "-keep public class * extends androidx.recyclerview.widget.RecyclerView$LayoutManager {"
seen = False
for path in root.glob("lp/**/proguard.txt"):
    text = path.read_text()
    if "androidx.recyclerview.widget.RecyclerView" not in text:
        continue
    seen = True
    if needle not in text:
        continue
    start = text.index("# When layoutManager")
    end = text.index("-keepclassmembers class androidx.recyclerview.widget.RecyclerView")
    path.write_text(text[:start] + text[end:])
if not seen:
    raise SystemExit("recyclerview proguard rules were not extracted")
