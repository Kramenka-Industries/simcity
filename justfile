export NUCLEAR_OPTION_GAME := env_var_or_default("NUCLEAR_OPTION_GAME", home_directory() + "/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Nuclear Option")

# Compile against the assemblies in the selected game installation.
build:
    #!/usr/bin/env bash
    set -euo pipefail

    DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/simcity-dotnet}" dotnet build SimcityVL49Cargo.csproj -v:minimal

# Build and copy the plugin into the selected game's BepInEx folder.
install: build
    #!/usr/bin/env bash
    set -euo pipefail

    plugin_dir="$NUCLEAR_OPTION_GAME/BepInEx/plugins"
    if [[ ! -d "$plugin_dir" ]]; then
      printf 'Missing BepInEx plugins directory: %s\n' "$plugin_dir" >&2
      exit 1
    fi

    install -m 644 bin/SimcityVL49Cargo.dll "$plugin_dir/SimcityVL49Cargo.dll"
    printf '%s\n' "$plugin_dir/SimcityVL49Cargo.dll"

# Inspect game assembly types, fields, method signatures, and optional IL.
inspect type method="":
    #!/usr/bin/env bash
    set -euo pipefail

    DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/simcity-dotnet}" dotnet run --project tools/AssemblyInspector/AssemblyInspector.csproj -p:GameDir="$NUCLEAR_OPTION_GAME" -- "$NUCLEAR_OPTION_GAME/NuclearOption_Data/Managed/Assembly-CSharp.dll" "{{type}}" "{{method}}"
