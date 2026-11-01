#!/bin/sh
# Builds the native library for Linux (.so) or macOS (.dylib) into native/out.
# Needs gcc (or clang installed as gcc). Run from anywhere.
set -e
cd "$(dirname "$0")"
mkdir -p out
case "$(uname -s)" in
  Darwin) gcc -dynamiclib -O2 -Wall -Wextra -o out/libdeliveryquotes_native.dylib dq_surcharge.c ;;
  *)      gcc -shared -fPIC -O2 -Wall -Wextra -o out/libdeliveryquotes_native.so dq_surcharge.c ;;
esac
echo "native library built in native/out"
