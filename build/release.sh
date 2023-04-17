#!/bin/bash
set -e
APP_NAME_BASE="MergeSynced"
APP_NAME="$APP_NAME_BASE.app"
PROJECT_PATH="../MergeSynced/MergeSynced.csproj"
BIN_RELEASE_PATH="../MergeSynced/bin/Release/net7.0"
ENTITLEMENTS="$APP_NAME_BASE.entitlements"
SIGNING_IDENTITY="Jan Arnold"
SELECTED_PLATFORM=""

signCode () {
    find "$APP_NAME/Contents/MacOS/"|while read fname; do
        if [[ -f $fname ]]; then
            echo "[INFO] Signing $fname"
            codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGNING_IDENTITY" "$fname"
        fi
    done

    echo "[INFO] Signing app file"
    codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGNING_IDENTITY" "$APP_NAME"
}

compile () {
    if [[ -z "${SELECTED_PLATFORM}" ]]; then
        echo Please select platform to compile for
        exit 1
    fi
    if [[ -z "${APP_NAME_BASE}" ]]; then
        echo Please set base app name
        exit 1
    fi
    echo Clean up pre compile...
    dotnet clean $PROJECT_PATH
    rm -rf "$APP_NAME_BASE"
    rm -rf "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip"

    echo Building for $SELECTED_PLATFORM...
    echo "dotnet publish $PROJECT_PATH -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release"
    dotnet publish $PROJECT_PATH -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release

    echo Copy $APP_NAME_BASE and rename folder
    cp -R $BIN_RELEASE_PATH/$SELECTED_PLATFORM/publish "$APP_NAME_BASE"

    if test -f "$APP_NAME_BASE/libAvaloniaNative.dylib"; then
        echo Adding icon to executable
        sips -i "$APP_NAME_BASE/Assets/MergeSyncedLogoSimple.icns"
        DeRez -only icns "$APP_NAME_BASE/Assets/MergeSyncedLogoSimple.icns" > tmpicns.rsrc
        Rez -append tmpicns.rsrc -o "$APP_NAME_BASE/$APP_NAME_BASE"
        SetFile -a C "$APP_NAME_BASE/$APP_NAME_BASE"
        rm -rf tmpicns.rsrc
    fi
    rm -rf "$APP_NAME_BASE/Assets"

    echo Create release zip...
    if test -f "$APP_NAME_BASE/libAvaloniaNative.dylib"; then
        ditto -c -k --sequesterRsrc --keepParent "$APP_NAME_BASE" "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip"
    else
        zip -r "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip" "$APP_NAME_BASE" -x '**/.*' -x '**/__MACOSX'
    fi

    echo Cleanup for $SELECTED_PLATFORM...
    rm -rf $APP_NAME_BASE
    dotnet clean $PROJECT_PATH
    echo Done creating ${APP_NAME_BASE}_$SELECTED_PLATFORM.zip
}

compileBundle () {
    if [[ -z "${SELECTED_PLATFORM}" ]]; then
        echo Please select platform to compile for
        exit 1
    fi
    if [[ -z "${APP_NAME}" ]]; then
        echo Please set app name
        exit 1
    fi
    if [[ -z "${APP_NAME_BASE}" ]]; then
        echo Please set base app name
        exit 1
    fi
    echo Clean up pre compile...
    dotnet clean $PROJECT_PATH
    rm -rf "$APP_NAME"
    rm -rf "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip"

    echo Building for $SELECTED_PLATFORM...
    echo "dotnet publish $PROJECT_PATH -t:BundleApp -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release"
    dotnet publish $PROJECT_PATH -t:BundleApp -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release

    echo Copy $APP_NAME and rename folder
    cp -R $BIN_RELEASE_PATH/$SELECTED_PLATFORM/publish/$APP_NAME "$APP_NAME"

    echo Start code signing...
    signCode

    echo Create release zip...
    ditto -c -k --sequesterRsrc --keepParent "$APP_NAME" "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip"
    notarize

    echo Cleanup for $SELECTED_PLATFORM...
    rm -rf $APP_NAME
    dotnet clean $PROJECT_PATH
    echo Done creating ${APP_NAME_BASE}_$SELECTED_PLATFORM.zip
}

notarize (){
    while true; do
        read -p "Do you wish to notarize ${APP_NAME_BASE}_$SELECTED_PLATFORM.zip? " yn
        case $yn in
            [Yy]* ) xcrun altool --notarize-app -f "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip" --primary-bundle-id com.coraxx.mergesynched-$SELECTED_PLATFORM -u "@keychain:AC_PASSWORD" -p "@keychain:AC_PASSWORD"; break;;
            [Nn]* ) echo "Skipping..."; break;;
            * ) echo "Please answer yes or no.";;
        esac
    done
    
}
##################
# Build releases #
##################

##################################################
echo Building $APP_NAME for MacOs X...
## x64 ###########################################
SELECTED_PLATFORM="osx-x64"
compile
#read -p "Press enter to continue"

## arm64 #########################################
SELECTED_PLATFORM="osx-arm64"
compile
#read -p "Press enter to continue"

##################################################
echo Building $APP_NAME for Linux...
## x64 ###########################################
APP_NAME="$APP_NAME_BASE"
SELECTED_PLATFORM="linux-x64"
compile
#read -p "Press enter to continue"

## arm64 #########################################
APP_NAME="$APP_NAME_BASE"
SELECTED_PLATFORM="linux-arm64"
compile
#read -p "Press enter to continue"
