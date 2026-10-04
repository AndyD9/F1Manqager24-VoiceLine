-- Loads F1VoiceMapper.dll (plain Win32, no UE4SS C++ ABI dependency).
-- The DLL does all the work from DllMain and logs to F1VoiceMapper.log.
local candidates = {
    "ue4ss/Mods/F1VoiceMapper/F1VoiceMapper.dll",
    "Mods/F1VoiceMapper/F1VoiceMapper.dll",
}

local source = debug.getinfo(1, "S").source
local scriptDir = source:match("^@?(.*[\\/])Scripts[\\/]")
if scriptDir then
    table.insert(candidates, 1, scriptDir .. "F1VoiceMapper.dll")
end

for _, path in ipairs(candidates) do
    local ok, err = package.loadlib(path, "*")
    if ok then
        print("[F1VoiceMapper] loaded " .. path .. "\n")
        return
    end
    print("[F1VoiceMapper] could not load " .. path .. ": " .. tostring(err) .. "\n")
end
