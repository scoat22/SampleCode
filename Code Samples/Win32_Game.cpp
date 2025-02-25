// Note: Not a final platform layer, but gets us started with an arbitrary game. Demonstrates knowledge of basic Win32 API calls.

#include "Game.h"
#include "Vulkan_Game.cpp"

#include <windows.h>
#include <stdio.h>
#include <malloc.h>
#include <xinput.h>
#include <dsound.h>

#include "Win32_Game.h"

#define internal static 
#define local_persist static
#define global_variable static

#define Pi32 3.14159265359f;

// Todo: this is a global for now
global_variable bool GlobalRunning;
global_variable bool GlobalPause;
global_variable win32_offscreen_buffer GlobalBackBuffer;
global_variable LPDIRECTSOUNDBUFFER GlobalSecondaryBuffer;
global_variable int64 GlobalPerfCountFrequency;


// Note: XInputGetState
#define X_INPUT_GET_STATE(name) DWORD WINAPI name(DWORD dwUserIndex, XINPUT_STATE *pState)
typedef X_INPUT_GET_STATE(x_input_get_state);
X_INPUT_GET_STATE(XInputGetStateStub)
{
    return(ERROR_DEVICE_NOT_CONNECTED);
}
global_variable x_input_get_state* XInputGetState_ = XInputGetStateStub;
#define XInputGetState XInputGetState_

// Note: XInputSetState
#define X_INPUT_SET_STATE(name) DWORD WINAPI name(DWORD dwUserIndex, XINPUT_VIBRATION *pVibration)
typedef X_INPUT_SET_STATE(x_input_set_state);
X_INPUT_SET_STATE(XInputSetStateStub)
{
    return(ERROR_DEVICE_NOT_CONNECTED);
}
global_variable x_input_set_state* XInputSetState_ = XInputSetStateStub;
#define XInputSetState XInputSetState_

typedef DWORD WINAPI x_input_get_state(DWORD dwUserIndex, XINPUT_STATE* pState);
typedef DWORD WINAPI x_input_set_state(DWORD dwUserIndex, XINPUT_VIBRATION* pVibration);

#define DIRECT_SOUND_CREATE(name) HRESULT WINAPI name(LPCGUID pcGuidDevice, LPDIRECTSOUND* ppDS, LPUNKNOWN pUnkOuter);
typedef DIRECT_SOUND_CREATE(direct_sound_create);


DEBUG_PLATFORM_FREE_FILE_MEMORY(DEBUGPlatformFreeFileMemory)
{
    if (Memory)
    {
        VirtualFree(Memory, 0, MEM_RELEASE);
    }
}

DEBUG_PLATFORM_READ_ENTIRE_FILE(DEBUGPlatformReadEntireFile)
{
    debug_read_file_result Result = {};

    HANDLE FileHandle = CreateFileA(Filename, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, 0, 0);
    if (FileHandle != INVALID_HANDLE_VALUE)
    {
        LARGE_INTEGER FileSize;
        if (GetFileSizeEx(FileHandle, &FileSize))
        {
            uint32 FileSize32 = SafeTruncateUInt64(FileSize.QuadPart);
            Result.Contents = VirtualAlloc(0, FileSize.QuadPart, MEM_RESERVE|MEM_COMMIT, PAGE_READWRITE);
            if (Result.Contents)
            {
                DWORD BytesRead;
                if (ReadFile(FileHandle, Result.Contents, FileSize32, &BytesRead, 0) &&
                    (FileSize32 == BytesRead))
                {
                    // Note: File read successfully.
                    Result.ContentsSize = FileSize32;
                }
                else
                {
                    DEBUGPlatformFreeFileMemory(Result.Contents);
                    Result.Contents = 0;
                }
            }
            else
            {
                // Todo: Logging.
            }
        }
        else
        {
            // Todo: Logging.
        }

        CloseHandle(FileHandle);
    }
    else
    {
        // Todo: Logging.
    }
    return (Result);
}

DEBUG_PLATFORM_WRITE_ENTIRE_FILE(DEBUGPlatformWriteEntireFile)
{
    bool32 Result = false;

    HANDLE FileHandle = CreateFileA(Filename, GENERIC_WRITE, 0, 0, CREATE_ALWAYS, 0, 0);
    if (FileHandle != INVALID_HANDLE_VALUE)
    {
        DWORD BytesWritten;
        if (WriteFile(FileHandle, Memory, MemorySize, &BytesWritten, 0))
        {
            // Note: File read successfully.
            Result = (BytesWritten == MemorySize);
        }
        else
        {
            // Todo: Logging.
        }

        CloseHandle(FileHandle);
    }
    else
    {
        // Todo: Logging.
    }
    return(Result);
}

inline FILETIME
Win32GetLastWriteTime(char* Filename)
{
    FILETIME LastWriteTime = {};

    WIN32_FIND_DATA FindData;
    HANDLE FindHandle = FindFirstFileA(Filename, &FindData);
    if (FindHandle != INVALID_HANDLE_VALUE)
    {
        LastWriteTime = FindData.ftLastWriteTime;
        FindClose(FindHandle);
    }

    return(LastWriteTime);
}

internal win32_game_code
Win32LoadGameCode(char* SourceDLLName)
{
    win32_game_code Result = {};

    // Todo: Need to get the proper path here
    // Todo: Automatic determination of when updates are necessary.

    char* TempDLLName = (char*)"game_Temp.dll";

    Result.DLLLastWriteTime = Win32GetLastWriteTime(SourceDLLName);
    CopyFileA(SourceDLLName, TempDLLName, FALSE);
    Result.GameCodeDLL = LoadLibraryA(TempDLLName);
    if (Result.GameCodeDLL)
    {
        Result.UpdateAndRender = (game_update_and_render*)
            GetProcAddress(Result.GameCodeDLL, "GameUpdateAndRender");

        Result.IsValid = (Result.UpdateAndRender != 0);
    }

    if (!Result.IsValid)
    {
        OutputDebugStringA("Failed to load Game DLL.\n");

        Result.UpdateAndRender = GameUpdateAndRenderStub;
    }
    return(Result);
}

internal void 
Win32UnloadGameCode(win32_game_code* GameCode)
{
    if (GameCode->GameCodeDLL) {

        FreeLibrary(GameCode->GameCodeDLL);
        GameCode->GameCodeDLL = 0;
    }

    GameCode->IsValid = false;
    GameCode->UpdateAndRender = GameUpdateAndRenderStub;
}

internal void
Win32LoadXInput(void)
{
    HMODULE XInputLibrary = LoadLibraryA("xinput1_4.dll");
    if (!XInputLibrary)
    {
        // Todo: Diagnostic.
        XInputLibrary = LoadLibraryA("xinput1_3.dll");
    }
    if (XInputLibrary)
    {
        XInputGetState = (x_input_get_state*)GetProcAddress(XInputLibrary, "XInputGetState");
        if (!XInputGetState) { XInputGetState = XInputGetStateStub; }

        XInputSetState = (x_input_set_state*)GetProcAddress(XInputLibrary, "XInputSetState");
        if (!XInputSetState) { XInputSetState = XInputSetStateStub; }

        // Todo: Diagnostic.
    }
    else
    {
        // Todo: Diagnostic.
    }
}

internal bool
Win32InitDSound(HWND Window, int32 SamplesPerSecond, int32 BufferSize)
{
    // Note: Load the library.
    HMODULE DSoundLibrary = LoadLibraryA("dsound.dll");

    if (DSoundLibrary)
    {
        // Note: Get a DirectSound object. - cooperative
        direct_sound_create* DirectSoundCreate = (direct_sound_create*)
            GetProcAddress(DSoundLibrary, "DirectSoundCreate");

        if (DirectSoundCreate)
        {
            LPDIRECTSOUND DirectSound;
            if (SUCCEEDED(DirectSoundCreate(0, &DirectSound, 0)))
            {
                WAVEFORMATEX WaveFormat = {};
                WaveFormat.wFormatTag = WAVE_FORMAT_PCM;
                WaveFormat.nChannels = 2;
                WaveFormat.nSamplesPerSec = SamplesPerSecond;
                WaveFormat.wBitsPerSample = 16;
                WaveFormat.nBlockAlign = (WaveFormat.nChannels * WaveFormat.wBitsPerSample) / 8;
                WaveFormat.nAvgBytesPerSec = WaveFormat.nSamplesPerSec * WaveFormat.nBlockAlign;
                WaveFormat.cbSize = 0;

                if (SUCCEEDED(DirectSound->SetCooperativeLevel(Window, DSSCL_PRIORITY)))
                {
                    DSBUFFERDESC BufferDescription = { };
                    BufferDescription.dwSize = sizeof(BufferDescription);
                    BufferDescription.dwFlags = DSBCAPS_PRIMARYBUFFER;


                    // Note: "Create" a primary buffer.
                    LPDIRECTSOUNDBUFFER PrimaryBuffer;
                    if (SUCCEEDED(DirectSound->CreateSoundBuffer(&BufferDescription, &PrimaryBuffer, 0)))
                    {
                        if (SUCCEEDED(PrimaryBuffer->SetFormat(&WaveFormat)))
                        {
                            // Note: We finally set the format!
                            
                        }
                    }
                    else
                    {
                        OutputDebugStringA("Failed to create SoundBuffer.\n");
                    }
                }
                else
                {
                    // Todo: Diagnostic.
                    OutputDebugStringA("Failed to set cooperative level.\n");
                }

                DSBUFFERDESC BufferDescription = { };
                BufferDescription.dwSize = sizeof(BufferDescription);
                BufferDescription.dwFlags = 0;
                BufferDescription.dwBufferBytes = BufferSize;
                BufferDescription.lpwfxFormat = &WaveFormat;
                // Note: "Create" a secondary buffer. 
                if (SUCCEEDED(DirectSound->CreateSoundBuffer(&BufferDescription, &GlobalSecondaryBuffer, 0)))
                {
                    // Note: Start it playing.
                }
                OutputDebugStringA("Sound is working\n");
                return true;
            }
            else
            {
                // Todo: Diagnostic.
                OutputDebugStringA("Failed to create DirectSound (You probably had no speakers plugged in).\n");
            }
        }
        else
        {
            OutputDebugStringA("DirectSound object was null.\n");
        }
    }
    else
    {
        // Todo: Diagnostic.
        OutputDebugStringA("Failed to load DSound Library.\n");
    }
    return false;
}

internal win32_window_dimension 
Win32GetWindowDimension(HWND Window)
{
    win32_window_dimension Result;

    RECT ClientRect;
    GetClientRect(Window, &ClientRect);
    Result.Width = ClientRect.right - ClientRect.left;
    Result.Height = ClientRect.bottom - ClientRect.top;


    return (Result);
}


internal void
Win32ResizeDIBSection(win32_offscreen_buffer* Buffer, int Width, int Height)
{
    if (Buffer->Memory)
    {
        VirtualFree(Buffer->Memory, 0, MEM_RELEASE);
    }

    Buffer->Width = Width;
    Buffer->Height = Height;
    Buffer->BytesPerPixel = 4;

    Buffer->Info.bmiHeader.biSize = sizeof(Buffer->Info.bmiHeader);
    Buffer->Info.bmiHeader.biWidth = Buffer->Width;
    Buffer->Info.bmiHeader.biHeight = -Buffer->Height;
    Buffer->Info.bmiHeader.biPlanes = 1;
    Buffer->Info.bmiHeader.biBitCount = 32;
    Buffer->Info.bmiHeader.biCompression = BI_RGB;

    int BitmapMemorySize = (Width * Height) * Buffer->BytesPerPixel;
    Buffer->Memory = VirtualAlloc(0, BitmapMemorySize, MEM_RESERVE|MEM_COMMIT, PAGE_READWRITE);
    Buffer->Pitch = Width * Buffer->BytesPerPixel;
}

internal void 
Win32CopyBufferToWindow(win32_offscreen_buffer* Buffer, HDC DeviceContext, int WindowWidth, int WindowHeight)
{
    // Todo: Aspect ratio correction.
    // 
    // Copy one rectangle to another rectangle
    StretchDIBits(DeviceContext,
        0, 0, WindowWidth, WindowHeight,
        0, 0, Buffer->Width, Buffer->Height,
        Buffer->Memory,
        &Buffer->Info,
        DIB_RGB_COLORS, SRCCOPY);
}

LRESULT CALLBACK
Win32MainWindowCallback(
    HWND Window,
    UINT Message,
    WPARAM WParam,
    LPARAM LParam)
{
    LRESULT Result = 0;

    switch (Message)
    {
        case WM_SIZE:
        {

        } break;
        case WM_CLOSE:
        {
            // Todo: handle this with a message to the user
            GlobalRunning = false;
        } break;
        case WM_ACTIVATEAPP:
        {
            
        } break;
        case WM_DESTROY:
        {
            // Todo: handle this as an error - recreate window?
            GlobalRunning = false;
        } break;
        case WM_SYSKEYDOWN:
        {

        } break;
        case WM_SYSKEYUP:
        {

        } break;
        case WM_KEYDOWN:
        {

        } break;
        case WM_KEYUP:
        {
            Assert("Keyboard input came in through a non-dispatch message");
        } break;
        case WM_PAINT:
        {
            PAINTSTRUCT Paint;
            HDC DeviceContext = BeginPaint(Window, &Paint);
            win32_window_dimension Dimension = Win32GetWindowDimension(Window);
            //Win32CopyBufferToWindow(&GlobalBackBuffer, DeviceContext, Dimension.Width, Dimension.Height);
            Win32CopyBufferToWindow(&GlobalBackBuffer, DeviceContext, 1280, 720);
            EndPaint(Window, &Paint);
        } break;
        default:
        {
//            OutputDebugStringA("default\n");
            Result = DefWindowProcA(Window, Message, WParam, LParam);
        } break;
    }
    return (Result);
}

internal void
Win32ClearSoundBuffer(win32_sound_output* SoundOutput)
{
    VOID* Region1;
    DWORD Region1Size;
    VOID* Region2;
    DWORD Region2Size;
    if (SUCCEEDED(GlobalSecondaryBuffer->Lock(0, SoundOutput->SecondaryBufferSize,
        &Region1, &Region1Size,
        &Region2, &Region2Size,
        0)))
    {
        uint8* DestSample = (uint8*)Region1;
        for (DWORD ByteIndex = 0;
            ByteIndex < Region1Size;
            ++ByteIndex)
        {
            *DestSample++ = 0;
        }
        DestSample = (uint8*)Region2;
        for (DWORD ByteIndex = 0;
            ByteIndex < Region2Size;
            ++ByteIndex)
        {
            *DestSample++ = 0;
        }

        GlobalSecondaryBuffer->Unlock(Region1, Region1Size, Region2, Region2Size);
    }
}

internal void
Win32FillSoundBuffer(win32_sound_output* SoundOutput, DWORD ByteToLock, DWORD BytesToWrite,
    game_sound_output_buffer* SourceBuffer)
{
    VOID* Region1;
    DWORD Region1Size;
    VOID* Region2;
    DWORD Region2Size;
    if (SUCCEEDED(GlobalSecondaryBuffer->Lock(ByteToLock, BytesToWrite,
        &Region1, &Region1Size,
        &Region2, &Region2Size,
        0)))
    {
        DWORD Region1SampleCount = Region1Size / SoundOutput->BytesPerSample;
        int16* DestSample = (int16*)Region1;
        int16* SourceSample = SourceBuffer->Samples;
        for (DWORD SampleIndex = 0;
            SampleIndex < Region1SampleCount;
            ++SampleIndex)
        {
            *DestSample++ = *SourceSample++;
            *DestSample++ = *SourceSample++;
        }

        DWORD Region2SampleCount = Region2Size / SoundOutput->BytesPerSample;
        DestSample = (int16*)Region2;
        for (DWORD SampleIndex = 0;
            SampleIndex < Region2SampleCount;
            ++SampleIndex)
        {
            *DestSample++ = *SourceSample++;
            *DestSample++ = *SourceSample++;
        }

        GlobalSecondaryBuffer->Unlock(Region1, Region1Size, Region2, Region2Size);
    }
}

internal void
Win32ProcessKeyboardMessage(game_button_state* NewState, bool32 IsDown)
{
    if (NewState->EndedDown != IsDown)
    {
        NewState->EndedDown = IsDown;
        ++NewState->HalfTransitionCount;
    }
}

internal void
Win32ProcessXInputDigitalButton(WORD wButtons, game_button_state* OldState, DWORD ButtonBit,
                                game_button_state* NewState)
{
    NewState->EndedDown = (wButtons & ButtonBit);
    NewState->HalfTransitionCount = (OldState->EndedDown != NewState->EndedDown) ? 1 : 0;
}

inline LARGE_INTEGER
Win32GetWallClock()
{
    LARGE_INTEGER Result;
    QueryPerformanceCounter(&Result);
    return(Result);
}

inline real32
Win32GetSecondsElapsed(LARGE_INTEGER Start, LARGE_INTEGER End)
{
    real32 Result = (real32)(End.QuadPart - Start.QuadPart) / 
                                    (real32)GlobalPerfCountFrequency;
    return(Result);
}

int CALLBACK WinMain(
    _In_     HINSTANCE Instance,
    _In_opt_ HINSTANCE PrevInstance,
    _In_     LPSTR CommandLine,
    _In_     int ShowCode)
{
    LARGE_INTEGER PerfCountFrequencyResult;
    QueryPerformanceFrequency(&PerfCountFrequencyResult);
    GlobalPerfCountFrequency = PerfCountFrequencyResult.QuadPart;

    // Note: Set the Windows scheduler granularity to 1ms, 
    // so that our sleep can be more granular.
    UINT DesiredSchedulerMS = 1;
    bool32 SleepIsGranular = (timeBeginPeriod(DesiredSchedulerMS) == TIMERR_NOERROR);

    Win32LoadXInput();

    WNDCLASS WindowClass = {};

    Win32ResizeDIBSection(&GlobalBackBuffer, 1280, 720);

    WindowClass.style = CS_HREDRAW | CS_VREDRAW | CS_OWNDC; // CS_OWNDC here?
    WindowClass.lpfnWndProc = Win32MainWindowCallback;
    WindowClass.hInstance = Instance;
    //    WindowClass.hIcon;
    WindowClass.lpszClassName = "gameWindowClass";

    int MonitorRefreshHz = 60;
    int GameUpdateHz = MonitorRefreshHz;
    real32 TargetSecondsPerFrame = 1.0f / (real32)GameUpdateHz;

    if (RegisterClassA(&WindowClass))
    {
        HWND Window =
            CreateWindowExA(
                0,
                WindowClass.lpszClassName,
                "game",
                WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                0,
                0,
                Instance,
                0);
        if (Window)
        {
            HDC DeviceContext = GetDC(Window);

            Vulkan::HelloTriangleApplication VulkanApp;
            try {
                VulkanApp.InitVulkan(Window, Instance);
            }
            catch (const std::exception& e) {
                OutputDebugStringA(e.what());
                OutputDebugStringA("\n");
                GlobalPause = TRUE;
            }

            // Note: Sound test.
            win32_sound_output SoundOutput = {};
            SoundOutput.SamplesPerSecond = 48000;
            SoundOutput.BytesPerSample = sizeof(int16) * 2;
            SoundOutput.SecondaryBufferSize = SoundOutput.SamplesPerSecond * SoundOutput.BytesPerSample;
            SoundOutput.LatencySampleCount = SoundOutput.SamplesPerSecond / 15;
            bool SoundIsWorking = Win32InitDSound(Window, SoundOutput.SamplesPerSecond, SoundOutput.SecondaryBufferSize);
            if (SoundIsWorking == true) 
            { 
                Win32ClearSoundBuffer(&SoundOutput);
                GlobalSecondaryBuffer->Play(0, 0, DSBPLAY_LOOPING);
            }
            
            GlobalRunning = true;

            int16* Samples = (int16*)VirtualAlloc(0,SoundOutput.SecondaryBufferSize, 
                                                    MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        
#if game_INTERNAL
            LPVOID BaseAddress = (LPVOID)Terabytes(uint64(2));
#else
            LPVOID BaseAddress = 0;
#endif
            game_memory GameMemory = {}; // Wipe to 0
            GameMemory.PermanentStorageSize = Megabytes(64);
            GameMemory.TransientStorageSize = Gigabytes(1);
            GameMemory.DEBUGPlatformFreeFileMemory = DEBUGPlatformFreeFileMemory;
            GameMemory.DEBUGPlatformReadEntireFile = DEBUGPlatformReadEntireFile;
            GameMemory.DEBUGPlatformWriteEntireFile = DEBUGPlatformWriteEntireFile;

            uint64 TotalSize = GameMemory.PermanentStorageSize + GameMemory.TransientStorageSize;
            GameMemory.PermanentStorage = VirtualAlloc(BaseAddress, (size_t)TotalSize,
                                            MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            GameMemory.TransientStorage = (uint8*)GameMemory.PermanentStorage + 
                                            GameMemory.PermanentStorageSize;

            if (Samples && GameMemory.PermanentStorage && GameMemory.TransientStorage)
            {
                game_input Input[2] = {};
                game_input* NewInput = &Input[0];
                game_input* OldInput = &Input[1];
                NewInput->SecondsToAdvanceOverUpdate = TargetSecondsPerFrame;

                LARGE_INTEGER LastCounter = Win32GetWallClock();
                
                char* SourceDLLName = (char*)"game.dll";
                win32_game_code Game = Win32LoadGameCode(SourceDLLName);

                uint64 LastCycleCount = __rdtsc();
                while (GlobalRunning)
                {
                    // Note: Support for live code reloading.
                    FILETIME NewDLLWriteTime = Win32GetLastWriteTime(SourceDLLName);
                    if (CompareFileTime(&NewDLLWriteTime, &Game.DLLLastWriteTime) != 0)
                    {
                        Win32UnloadGameCode(&Game);
                        Game = Win32LoadGameCode(SourceDLLName);
                    }

                    LARGE_INTEGER BeginCounter;
                    QueryPerformanceCounter(&BeginCounter);
                    BeginCounter.QuadPart;

                    MSG Message;

                    game_controller_input* OldKeyboardController = &OldInput->Controllers[0];
                    game_controller_input* NewKeyboardController = &NewInput->Controllers[0];
                    game_controller_input ZeroController = {};
                    *NewKeyboardController = ZeroController;
                    for (int ButtonIndex = 0;
                        ButtonIndex < ArrayCount(NewKeyboardController->Buttons);
                        ++ButtonIndex)
                    {
                        NewKeyboardController->Buttons[ButtonIndex].EndedDown = 
                            OldKeyboardController->Buttons[ButtonIndex].EndedDown;
                    }

                    while (PeekMessageA(&Message, 0, 0, 0, PM_REMOVE))
                    {
                        if (Message.message == WM_QUIT)
                        {
                            GlobalRunning = false;
                        }

                        switch (Message.message)
                        {
                        case WM_SYSKEYDOWN:
                        case WM_SYSKEYUP:
                        case WM_KEYDOWN:
                        {
                            uint32 VKCode = (uint32)Message.wParam;

                            if (VKCode == VK_MBUTTON)
                            {
                                // 
                                //Win32ProcessKeyboardMessage(&NewInput->MouseButtons[0], true);
                            }
                            if (VKCode == VK_LBUTTON)
                            {
                                // Left-click
                                //NewKeyboardController->Buttons[0].EndedDown = true;
                                Win32ProcessKeyboardMessage(&NewInput->MouseButtons[0], true);
                                OutputDebugStringA("Left-click.\n");
                            }
                            if (VKCode == VK_RBUTTON)
                            {
                                // Right-click
                                //NewKeyboardController->Buttons[1].EndedDown = true;
                                Win32ProcessKeyboardMessage(&NewInput->MouseButtons[1], true);
                                OutputDebugStringA("Right-click.\n");
                            }
                            if (VKCode == VK_XBUTTON1)
                            {
                                // 
                            }
                            if (VKCode == VK_XBUTTON2)
                            {
                                // 
                            }
                            if (VKCode == 'W')
                            {
                                NewKeyboardController->Vertical = -1;
                            }
                            if (VKCode == 'S')
                            {
                                NewKeyboardController->Vertical = 1;
                            }
                            if (VKCode == 'A')
                            {
                                NewKeyboardController->Horizontal = -1;
                            }
                            if (VKCode == 'D')
                            {
                                NewKeyboardController->Horizontal = 1;
                            }
                            if (VKCode == 'D')
                            {
                                NewKeyboardController->Horizontal = 1;
                            }
                        } break;
                        case WM_KEYUP:
                        {
                            uint32 VKCode = (uint32)Message.wParam;
                            bool WasDown = ((Message.lParam & (1 << 30)) != 0);
                            bool IsDown = ((Message.lParam & (1 << 31)) == 0);
                            if (WasDown != IsDown)
                            {
                                if (VKCode == 'W')
                                {
                                    //OutputDebugStringA("W Key\n");
                                }
                                else if (VKCode == 'A')
                                {

                                }
                                else if (VKCode == 'S')
                                {

                                }
                                else if (VKCode == 'D')
                                {

                                }
                                else if (VKCode == 'Q')
                                {

                                }
                                else if (VKCode == 'E')
                                {

                                }
                                else if (VKCode == 'P')
                                {
                                    GlobalPause = !GlobalPause;
                                }
                                else if (VKCode == VK_UP)
                                {
                                    Win32ProcessKeyboardMessage(&NewKeyboardController->Up, IsDown);
                                }
                                else if (VKCode == VK_DOWN)
                                {
                                    Win32ProcessKeyboardMessage(&NewKeyboardController->Down, IsDown);
                                }
                                else if (VKCode == VK_LEFT)
                                {
                                    Win32ProcessKeyboardMessage(&NewKeyboardController->Left, IsDown);
                                }
                                else if (VKCode == VK_RIGHT)
                                {
                                    Win32ProcessKeyboardMessage(&NewKeyboardController->Right, IsDown);
                                }
                                else if (VKCode == VK_ESCAPE)
                                {
                                    GlobalRunning = false;
                                }
                                else if (VKCode == VK_SPACE)
                                {

                                }
                            }

                            bool32 AltKeyWasDown = (Message.lParam & (1 << 29));
                            if ((VKCode == VK_F4) && AltKeyWasDown)
                                //if (VKCode == VK_F4)
                            {
                                GlobalRunning = false;
                            }
                        } break;
                        default:
                        {
                            TranslateMessage(&Message);
                            DispatchMessageA(&Message);
                        } break;
                        }
                    }

                    if (!GlobalPause)
                    {
                        POINT MousePosition;
                        GetCursorPos(&MousePosition);
                        if (!ScreenToClient(Window, &MousePosition))
                        {
                            OutputDebugStringA("Failed to get screen client\n");
                        }
                        NewInput->MouseX = MousePosition.x;
                        NewInput->MouseY = MousePosition.y;
                        Win32ProcessKeyboardMessage(&NewInput->MouseButtons[0], GetKeyState(VK_LBUTTON) & (1 << 15));
                        Win32ProcessKeyboardMessage(&NewInput->MouseButtons[1], GetKeyState(VK_MBUTTON)& (1 << 15));
                        Win32ProcessKeyboardMessage(&NewInput->MouseButtons[2], GetKeyState(VK_RBUTTON)& (1 << 15));
                        Win32ProcessKeyboardMessage(&NewInput->MouseButtons[3], GetKeyState(VK_XBUTTON1)& (1 << 15));
                        Win32ProcessKeyboardMessage(&NewInput->MouseButtons[4], GetKeyState(VK_XBUTTON2)& (1 << 15));

                        // Todo: Should we poll this more frequently?
                        DWORD MaxControllerCount = XUSER_MAX_COUNT;
                        if (MaxControllerCount > ArrayCount(NewInput->Controllers))
                        {
                            MaxControllerCount = ArrayCount(NewInput->Controllers);
                        }

                        for (DWORD ControllerIndex = 0;
                            ControllerIndex < MaxControllerCount;
                            ControllerIndex++)
                        {
                            game_controller_input* OldController = &OldInput->Controllers[ControllerIndex];
                            game_controller_input* NewController = &NewInput->Controllers[ControllerIndex];

                            XINPUT_STATE ControllerState;
                            DWORD dwResult = XInputGetState(ControllerIndex, &ControllerState);

                            if (dwResult == ERROR_SUCCESS)
                            {
                                // Note: Controller is connected
                                XINPUT_GAMEPAD* GamePad = &ControllerState.Gamepad;

                                bool Up = (GamePad->wButtons & XINPUT_GAMEPAD_DPAD_UP);
                                bool Down = (GamePad->wButtons & XINPUT_GAMEPAD_DPAD_DOWN);
                                bool Left = (GamePad->wButtons & XINPUT_GAMEPAD_DPAD_LEFT);
                                bool Right = (GamePad->wButtons & XINPUT_GAMEPAD_DPAD_RIGHT);


                                uint16 StickX = GamePad->sThumbLX;
                                uint16 StickY = GamePad->sThumbLY;

                                Win32ProcessXInputDigitalButton(GamePad->wButtons,
                                    &OldController->Down, XINPUT_GAMEPAD_A,
                                    &NewController->Down);


                                bool Start = (GamePad->wButtons & XINPUT_GAMEPAD_START);
                                bool Back = (GamePad->wButtons & XINPUT_GAMEPAD_BACK);
                                bool LeftShoulder = (GamePad->wButtons & XINPUT_GAMEPAD_LEFT_SHOULDER);
                                bool RightShoulder = (GamePad->wButtons & XINPUT_GAMEPAD_RIGHT_SHOULDER);
                                bool AButton = (GamePad->wButtons & XINPUT_GAMEPAD_A);
                                bool BButton = (GamePad->wButtons & XINPUT_GAMEPAD_B);
                                bool XButton = (GamePad->wButtons & XINPUT_GAMEPAD_X);
                                bool YButton = (GamePad->wButtons & XINPUT_GAMEPAD_Y);
                            }
                            else
                            {
                                // Note: Controller is not connected.
                            }
                        }

                        // Note: DirectOuput output test.
                        DWORD ByteToLock = 0;
                        DWORD TargetCursor;
                        DWORD BytesToWrite = 0;
                        DWORD PlayCursor;
                        DWORD WriteCursor;
                        bool32 SoundIsValid = false;
                        // Todo: Tighten up sound logic so that we know where we should be writing to and can
                        // anticipate the time spent in the game update.
                        if (SoundIsWorking && SUCCEEDED(GlobalSecondaryBuffer->GetCurrentPosition(&PlayCursor, &WriteCursor)))
                        {
                            ByteToLock = (SoundOutput.RunningSampleIndex * SoundOutput.BytesPerSample) %
                                SoundOutput.SecondaryBufferSize;

                            TargetCursor =
                                ((PlayCursor +
                                    (SoundOutput.LatencySampleCount * SoundOutput.BytesPerSample)) %
                                    SoundOutput.SecondaryBufferSize);
                            if (ByteToLock > TargetCursor)
                            {
                                BytesToWrite = (SoundOutput.SecondaryBufferSize - ByteToLock);
                                BytesToWrite += TargetCursor;
                            }
                            else
                            {
                                BytesToWrite = TargetCursor - ByteToLock;
                            }
                            SoundIsValid = true;
                        }
                        game_sound_output_buffer SoundBuffer = {};
                        SoundBuffer.SamplesPerSecond = SoundOutput.SamplesPerSecond;
                        SoundBuffer.SampleCount = BytesToWrite / SoundOutput.BytesPerSample;
                        SoundBuffer.Samples = Samples;

                        game_offscreen_buffer Buffer = {};
                        Buffer.Memory = GlobalBackBuffer.Memory;
                        Buffer.Width = GlobalBackBuffer.Width;
                        Buffer.Height = GlobalBackBuffer.Height;
                        Buffer.Pitch = GlobalBackBuffer.Pitch;
                        Buffer.BytesPerPixel = GlobalBackBuffer.BytesPerPixel;
                        Game.UpdateAndRender(&GameMemory, NewInput, &Buffer, &SoundBuffer);

                        if (SoundIsWorking && SoundIsValid)
                        {
                            Win32FillSoundBuffer(&SoundOutput, ByteToLock, BytesToWrite, &SoundBuffer);
                        }

                        LARGE_INTEGER WorkCounter = Win32GetWallClock();
                        real32 WorkSecondsElapsed = Win32GetSecondsElapsed(LastCounter, WorkCounter);

                        // If we're going too fast, wait until we hit our target update rate
                        real32 SecondsElapsedForFrame = WorkSecondsElapsed;
                        if (SecondsElapsedForFrame < TargetSecondsPerFrame)
                        {
                            while (SecondsElapsedForFrame < TargetSecondsPerFrame)
                            {
                                if (SleepIsGranular)
                                {
                                    DWORD SleepMS = (DWORD)(1000.0f * (TargetSecondsPerFrame - SecondsElapsedForFrame));
                                    Sleep(SleepMS);
                                }
                                SecondsElapsedForFrame = Win32GetSecondsElapsed(LastCounter, Win32GetWallClock());
                            }
                        }
                        else
                        {
                            // Note: we missed the target frame rate.
                            // Todo: Logging.
                        }

                        win32_window_dimension Dimension = Win32GetWindowDimension(Window);
                        //Win32CopyBufferToWindow(&GlobalBackBuffer, DeviceContext, Dimension.Width, Dimension.Height);
                        Win32CopyBufferToWindow(&GlobalBackBuffer, DeviceContext, 1280, 720);

#if 0
                        int32 MSPerFrame = (int32)((1000 * CounterElapsed) / GlobalPerfCountFrequency);
                        int32 FPS = (int32)(GlobalPerfCountFrequency / CounterElapsed);
                        int32 MCPF = (int32)(CyclesElapsed / (1000 * 1000));

                        char Buffer[256];
                        wsprintf(Buffer, "%dms/f,  %df/s, %dMc/fn\n", MSPerFrame, FPS, MCPF);
                        OutputDebugStringA(Buffer);
#endif
                        game_input* Temp = NewInput;
                        NewInput = OldInput;
                        OldInput = Temp;

                        LARGE_INTEGER EndCounter = Win32GetWallClock();
                        LastCounter = EndCounter;

                        uint64 EndCycleCount = __rdtsc();
                        uint64 CyclesElapsed = EndCycleCount - LastCycleCount;
                        LastCycleCount = EndCycleCount;
                    }
                }
                VulkanApp.OnDestroy();
            }
            else
            {
                // Todo: logging
            }
        }
        else
        {
            // Todo: logging
        }
    }
    else
    {
        // Todo: logging
    }

    return 0;
}