#!/bin/bash
# csc_overlay.sh <tree> [--release]
# Whole-Unity-project compile (same as tools/csc_full.sh) of the REAL checkout, with Assets/Scripts/WRLDZ taken from
# <tree> (a scratch_tree.sh copy). Use it to check that a patch built in a copy, or a commit (scratch_tree.sh <dir> HEAD),
# compiles together with the UI / Presentation code the copy does not contain. Prints errors only; exit 1 on any error.
set -u
T="$(cd "${1:?usage: csc_overlay.sh <tree> [--release]}" && pwd)"; shift || true
cd "$(dirname "$0")/../.."
U=/home/greatsaiyadave/Unity/Hub/Editor/6000.5.10f1/Editor/Data; M=$U/Managed/UnityEngine
REFS=""; for d in $M/*.dll; do REFS="$REFS -r:$d"; done
for d in Library/ScriptAssemblies/*.dll; do b=$(basename "$d"); case $b in Assembly-CSharp*|*Editor*|*.Tests.dll) ;; *) REFS="$REFS -r:$d";; esac; done
for d in $(find Assets -name "*.dll" -not -path "*/Editor/*" -not -path "*/win-x64/*" -not -name ocgcore.dll); do REFS="$REFS -r:$d"; done
SRCS="$(find Assets -name "*.cs" -not -path "*/Editor/*" -not -path "*/MobileDependencyResolver/*" -not -path "Assets/Scripts/WRLDZ/*") $(find "$T/Assets/Scripts/WRLDZ" -name "*.cs" -not -path "*/Editor/*")"
DEFS='UNITY_EDITOR;UNITY_EDITOR_LINUX;UNITY_STANDALONE_LINUX;UNITY_6000_0_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2023_1_OR_NEWER;ENABLE_INPUT_SYSTEM;UNITY_URP'
[ "${1:-}" == "--release" ] && DEFS='UNITY_ANDROID;UNITY_6000_0_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2023_1_OR_NEWER;ENABLE_INPUT_SYSTEM;UNITY_URP'
OUT=$($U/NetCoreRuntime/dotnet $U/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll -target:library -langversion:9 -nostdlib+ -noconfig \
  -nowarn:CS0168,CS0219,CS0414,CS0649,CS1998,CS0108,CS0162,CS0067,CS0169,CS0105,CS0618 -define:"$DEFS" $REFS -r:$U/NetStandard/ref/2.1.0/netstandard.dll \
  -out:"${TMPDIR:-/tmp}/argon_overlay_check_$$.dll" $SRCS 2>&1)
rm -f "${TMPDIR:-/tmp}/argon_overlay_check_$$.dll"
echo "$OUT" | grep -E "(^| )error " | sort -u | head -30
echo "$OUT" | grep -qE "(^| )error " && { echo "csc_overlay: FAIL"; exit 1; } || { echo "csc_overlay: OK"; exit 0; }
