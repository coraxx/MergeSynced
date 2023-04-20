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
    find "$APP_NAME_BASE/"|while read fname; do
        if [[ -f $fname ]]; then
            echo "[INFO] Signing $fname"
            codesign --force --timestamp --options=runtime --entitlements "$ENTITLEMENTS" --sign "$SIGNING_IDENTITY" "$fname"
        fi
    done
}

signBundle () {
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
    rm -rf "${APP_NAME_BASE}_$SELECTED_PLATFORM.dmg"

    echo Building for $SELECTED_PLATFORM...
    echo "dotnet publish $PROJECT_PATH -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release"
    dotnet publish $PROJECT_PATH -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release

    echo Copy $APP_NAME_BASE and rename folder
    cp -R $BIN_RELEASE_PATH/$SELECTED_PLATFORM/publish "$APP_NAME_BASE"

    # Check if packing mac version to copy image and start signing
    if test -f "$APP_NAME_BASE/libAvaloniaNative.dylib"; then
        # Signing not possible with icon
        # echo Adding icon to executable
        # sips -i "$APP_NAME_BASE/Assets/MergeSyncedLogoSimple.icns"
        # DeRez -only icns "$APP_NAME_BASE/Assets/MergeSyncedLogoSimple.icns" > tmpicns.rsrc
        # Rez -append tmpicns.rsrc -o "$APP_NAME_BASE/$APP_NAME_BASE"
        # SetFile -a C "$APP_NAME_BASE/$APP_NAME_BASE"
        # rm -rf tmpicns.rsrc
        rm -rf "$APP_NAME_BASE/Assets" # delete before signing
        # Sign files
        signCode
    fi
    rm -rf "$APP_NAME_BASE/Assets"

    echo Create release zip...
    if test -f "$APP_NAME_BASE/libAvaloniaNative.dylib"; then
        #ditto -c -k --sequesterRsrc --keepParent "$APP_NAME_BASE" "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip"
        # Create root folder for dmg
        mkdir dmgroot
        mv $APP_NAME_BASE dmgroot/
        mv dmgroot $APP_NAME_BASE
        create-dmg \
            --volname "$APP_NAME_BASE" \
            --volicon "../MergeSynced/Assets/MergeSyncedLogoSimple.icns" \
            --window-pos 200 120 \
            --window-size 800 400 \
            --icon-size 100 \
            --app-drop-link 600 185 \
            --codesign "Jan Arnold" \
            --notarize "AC_PASSWORD" \
            "${APP_NAME_BASE}_$SELECTED_PLATFORM.dmg" \
            "$APP_NAME_BASE"
        # Ask if zip should be notarized
        #notarize
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
    rm -rf "${APP_NAME_BASE}_$SELECTED_PLATFORM.dmg"

    echo Building for $SELECTED_PLATFORM...
    echo "dotnet publish $PROJECT_PATH -t:BundleApp -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release"
    dotnet publish $PROJECT_PATH -t:BundleApp -p:RuntimeIdentifier=$SELECTED_PLATFORM -p:Configuration=Release

    echo Copy $APP_NAME and rename folder
    cp -R $BIN_RELEASE_PATH/$SELECTED_PLATFORM/publish/$APP_NAME "$APP_NAME"

    echo Start code signing...
    signBundle

    echo Create release dmg...
    #ditto -c -k --sequesterRsrc --keepParent "$APP_NAME" "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip"
    create-dmg \
        --volname "$APP_NAME_BASE" \
        --volicon "../MergeSynced/Assets/MergeSyncedLogoSimple.icns" \
        --background "DmgBg.png" \
        --window-pos 200 120 \
        --window-size 550 400 \
        --icon-size 100 \
        --icon "$APP_NAME" 100 170 \
        --hide-extension "$APP_NAME" \
        --app-drop-link 400 170 \
        --codesign "Jan Arnold" \
        --notarize "AC_PASSWORD" \
        "${APP_NAME_BASE}_$SELECTED_PLATFORM.dmg" \
        "$APP_NAME"

    echo Cleanup for $SELECTED_PLATFORM...
    rm -rf $APP_NAME
    dotnet clean $PROJECT_PATH
    echo Done creating ${APP_NAME_BASE}_$SELECTED_PLATFORM.zip
}

notarize (){
    while true; do
        read -p "Do you wish to notarize ${APP_NAME_BASE}_$SELECTED_PLATFORM.zip? " yn
        case $yn in
            [Yy]* ) xcrun notarytool submit "${APP_NAME_BASE}_$SELECTED_PLATFORM.zip" --keychain-profile "AC_PASSWORD"; break;;
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
compileBundle
#read -p "Press enter to continue"

## arm64 #########################################
SELECTED_PLATFORM="osx-arm64"
compileBundle
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
