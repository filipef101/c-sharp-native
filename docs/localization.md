# Localization

Release keeps `InvariantGlobalization` on. The app does not carry ICU. Strings live in `StringTable`. `LocaleRuntime` reads the Android locale from `Configuration.Locales` and calls `StringTable.Use`. An intent extra `nativepoc_locale` overrides that for a run. The layout direction is `Rtl` when the tag is `ar`, `ar-XB`, or `rtl`, and `Ltr` otherwise.

`L.Get("Trips")` is a string resource. The English table is the key itself. `pt` and `pt-PT` use the European Portuguese table in `StringTable`. A missing key is the key. `ar-XB` and `en-XA` are pseudo-locales: `ar-XB` is right-to-left and both wrap the resolved string as `U+200F` plus brackets. They do not change the language table (`ar-XB` stays on the English strings).

`L.Format("Olá {0}", name)` replaces `{0}` through `{9}`. It does not format dates or use ICU. `L.Plural(count, one, other)` parses the count with the invariant culture and picks `one` when the count is 1 and `other` otherwise, then runs `Format`. That split matches the English and Portuguese strings in the trips sample. It is not a CLDR plural engine.

The trips sample (`TripsScreen`, `TripScreen`, `KitScreen`) reads its chrome and its sample row names through `L.Get`. The stored trip id stays `coast`. The visible name follows the locale. Effect assets try `name.pt.txt` before `name.txt` when the language is Portuguese. `weather.pt.txt` and `refresh.pt.txt` are those files.

The screen program stores the key. `OpRes` resolves it when the screen renders, so a locale change does not require a new program.
