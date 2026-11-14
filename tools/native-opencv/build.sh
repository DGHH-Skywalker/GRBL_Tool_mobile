#!/usr/bin/env bash
set -euo pipefail
abi="$1"
ndk="$2"
repo_root="$(pwd)"
work="$repo_root/artifacts/native-build-$abi"
out="$repo_root/artifacts/opencv-native/$abi"
mkdir -p "$work" "$out"
checkout() {
  git init -q "$1"
  git -C "$1" remote add origin "$2"
  git -C "$1" fetch -q --depth=1 origin "$3"
  git -C "$1" checkout -q --detach FETCH_HEAD
}
checkout "$work/opencv" https://github.com/opencv/opencv.git fe38fc608f6acb8b68953438a62305d8318f4fcd
checkout "$work/opencvsharp" https://github.com/shimat/opencvsharp.git 99b2313468074a2fc876e5a273659e9e2e34a6b2
curl --fail --location --retry 3 https://raw.githubusercontent.com/sdcb/opencvsharp-mini-runtime/9c4b7b52d17ae7764c28a2d21dc5bcf4de51e7bb/eng/opencvsharp.patch --output "$work/mini.patch"
git -C "$work/opencvsharp" apply "$work/mini.patch"
cmake -S "$work/opencv" -B "$work/cv-build" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$ndk/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI="$abi" -DANDROID_PLATFORM=24 -DANDROID_STL=c++_static \
  -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_INSTALL_PREFIX="$work/install" -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
  -DBUILD_LIST=core,imgproc,imgcodecs -DBUILD_SHARED_LIBS=OFF \
  -DBUILD_TESTS=OFF -DBUILD_PERF_TESTS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_JAVA=OFF \
  -DBUILD_ANDROID_PROJECTS=OFF -DBUILD_ANDROID_EXAMPLES=OFF \
  -DBUILD_opencv_python2=OFF -DBUILD_opencv_python3=OFF \
  -DWITH_OPENCL=OFF -DWITH_IPP=OFF -DWITH_ITT=OFF -DWITH_TBB=OFF \
  -DWITH_ADE=OFF -DBUILD_opencv_gapi=OFF -DWITH_PROTOBUF=OFF -DBUILD_PROTOBUF=OFF -DWITH_OPENJPEG=OFF -DWITH_JPEGXL=OFF \
  -DWITH_JPEG=OFF -DWITH_PNG=OFF -DWITH_TIFF=OFF -DWITH_WEBP=OFF \
  -DWITH_OPENEXR=OFF -DWITH_JASPER=OFF -DWITH_AVIF=OFF
cmake --build "$work/cv-build" --parallel 3
cmake --install "$work/cv-build"
config="$(find "$work/install" -name OpenCVConfig.cmake -print -quit)"
test -n "$config"
cmake -S "$work/opencvsharp/src" -B "$work/extern-build" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$ndk/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI="$abi" -DANDROID_PLATFORM=24 -DANDROID_STL=c++_static \
  -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_SHARED_LINKER_FLAGS='-Wl,-z,max-page-size=16384 -Wl,-z,common-page-size=16384' \
  -DCMAKE_DISABLE_FIND_PACKAGE_Tesseract=ON -DCMAKE_DISABLE_FIND_PACKAGE_Iconv=ON \
  -DOpenCV_DIR="$(dirname "$config")"
cmake --build "$work/extern-build" --parallel 3
cp "$work/extern-build/OpenCvSharpExtern/libOpenCvSharpExtern.so" "$out/"
"$ndk/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-strip" --strip-unneeded "$out/libOpenCvSharpExtern.so"
python3 tools/native-opencv/verify.py "$out/libOpenCvSharpExtern.so" "$abi"
cp "$work/opencv/LICENSE" "$out/OpenCV-LICENSE.txt"
cp "$work/opencvsharp/LICENSE" "$out/OpenCvSharp-LICENSE.txt"
(cd "$out" && sha256sum libOpenCvSharpExtern.so > SHA256SUMS.txt)
