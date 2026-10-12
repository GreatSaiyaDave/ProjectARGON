#!/usr/bin/env bash
# Create Tools/anime/.venv and install the measuring toolchain deps.
#   Tools/anime/setup.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV="$HERE/.venv"

if [[ ! -x "$VENV/bin/python" ]]; then
  echo "creating $VENV"
  python3 -m venv "$VENV"
fi

"$VENV/bin/python" -m pip install --quiet --disable-pip-version-check -r "$HERE/requirements.txt"
"$VENV/bin/python" - <<'EOF'
import numpy, PIL, cv2
print("ok  numpy", numpy.__version__, " pillow", PIL.__version__, " opencv", cv2.__version__)
EOF
echo "done. run scripts through $HERE/animepy"
