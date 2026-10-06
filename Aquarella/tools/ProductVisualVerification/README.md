# Product thumbnails

Owned Aquarella geometric illustrations, drawn offline by `generate_assets.py` (Pillow).
No external imagery, brands, API calls or third-party asset licences. No 3D runtime.
15 types × 6 palette variants; transparent WebP, 112px, intended for 44–52 CSS px.
Types: notebook, pencil, pen, ruler, eraser, scissors, glue, folder, paper, ball, car,
toy, gift, printer, generic. Shared representation deliberately favours recognition
at small sizes over exact product replication.

Regenerate: `python tools/ProductVisualVerification/generate_assets.py` (Pillow required
only by this offline tool; no application dependency). Verify from repository root:
`dotnet run --project Aquarella/tools/ProductVisualVerification -- Aquarella/Aquarella`.
The verification writes an isolated 100-item HTML fixture in the system temp directory,
never in a real business database. Serve that directory locally for visual inspection.

Classification accepts optional category/subtype for future consumers; current
StockProduct supplies only name and stable Guid. Colour uses FNV-1a of canonical Guid,
not GetHashCode or random. Renaming a product may change its type, never its colour.
Fallback is an own generic WebP embedded in component CSS (no extra network request),
behind the image, with Blazor onerror hiding a broken image. It remains available
even when the requested asset or the images directory cannot load. Deployment must
include component styles. No third-party images or licences.
CSS entry runs once on load, 380ms, without retained transforms, loops or hover motion.
Reduced motion renders the final state directly. No database/schema changes.

Verified: dotnet build (temporary output) 0 warnings / 0 errors; ProductVisualVerification
(100 products), PersistenceVerification, ReliabilityVerification, StockIntakeVerification,
SecurityVerification (isolated Production publish), legacy-recovery and metrics-motion
JavaScript tests. Transparent dimensions checked for all 90 files.
Pending: real browser Stock/Precios, responsive screenshot, reduced-motion playback,
asset-load-error interaction and scroll/CPU observation; no browser was available
in the implementation session. Fixtures index.html/precios.html are in system temp.
