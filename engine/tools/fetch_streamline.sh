#!/usr/bin/env bash
# Downloads NVIDIA Streamline's runtime for DLSS Frame Generation (frame_gen=dlss,
# docs/dlss_frame_generation.md) from NVIDIA's release of the Streamline SDK
# (github.com/NVIDIA-RTX/Streamline): sl.interposer.dll and the DLSS-G, Reflex and PCL plugins
# (MIT, license.txt there), nvngx_dlssg.dll (the NVIDIA RTX SDKs licence,
# nvngx_dlss.license.txt) and NvLowLatencyVk.dll (reflex.license.txt). They are not part of this
# repository. The engine loads them from next to the executable (out/ by default) or
# BB_STREAMLINE_DIR, only with frame_gen=dlss. Windows x64 only; production (unwatermarked) builds.
# The package ships the three licence texts in engine\licenses (package-template): build.ps1 keeps
# only programs in engine\out.
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
version=v2.14.16
dest=${1:-out}
files=(sl.interposer.dll sl.common.dll sl.dlss_g.dll sl.reflex.dll sl.pcl.dll nvngx_dlssg.dll
       NvLowLatencyVk.dll nvngx_dlss.license.txt reflex.license.txt)
mkdir -p "$dest"
missing=0
for file in "${files[@]}"; do
    [[ -s $dest/$file ]] || missing=1
done
if [[ $missing == 0 ]]; then
    echo "Streamline: $dest has the $version runtime"
    exit 0
fi
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
# The whole SDK (about 280 MB); only the runtime files above are kept.
curl -fSL --retry 3 -o "$work/sdk.zip" \
    "https://github.com/NVIDIA-RTX/Streamline/releases/download/$version/streamline-sdk-$version.zip"
# python3, or python where python3 is only the Windows Store stub.
py=python3
"$py" -c '' 2>/dev/null || py=python
"$py" - "$work/sdk.zip" "$dest" "${files[@]}" <<'EOF'
import os, sys, zipfile
archive, dest, names = sys.argv[1], sys.argv[2], sys.argv[3:]
with zipfile.ZipFile(archive) as z:
    for name in names:
        data = z.read('bin/x64/' + name)
        part = os.path.join(dest, name + '.part')
        with open(part, 'wb') as f:
            f.write(data)
        os.replace(part, os.path.join(dest, name))
    with open(os.path.join(dest, 'LICENSE-Streamline.txt'), 'wb') as f:
        f.write(z.read('license.txt'))
EOF
echo "Streamline: $version runtime in $dest"
