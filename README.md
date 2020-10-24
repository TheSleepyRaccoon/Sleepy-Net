# Sleepy-Net
A fast, simple to use, all in one networking system for c# (And Unity) 

## V0.2
- Low level wrapper for sockets, both UDP and TCP for raw data throughput that is easy to setup.

- Medium level wrapper for sockets, both UDP and TCP
  - Message based communication, fast and optimized conversion of packets to useful data
  - Statically bound message types
  - Dynamic on the fly messages and responses that have callbacks
    - Can also be done with Unity Coroutines also, allowing to yield on the request
  - Support for Large Messages 
    - Sending Files etc supported - Even UDP will auto-split, and ensure content delivery of large packets with progress callback
    
- High Level Wrapper (TCP) 
  - Network manager that will run inside unity scene, Allowing for Lobbies, Hosting and joining servers, Syncing objects etc.
  - Support for Basic Unity Components (Transform, Rigidbody, +More coming) 
  - Simple Interface to network your own classes
  - Scene Loading/Syncing support
  
- Full Encryption Support
  - RSA / AES communication can be enabled by default, and any message can be sent encrypted (even large files)
  
- Auto Port Forwarding Support
  - Using Open.Nat, Allows for *Most* Modern Routers to port-forward for you when creating servers
  
- Full network stats
  - Can track Packets Per Second, Data Per Second, Both sent and recv and also will keep totals for all stats. 

## Coming Next - V0.3
- Finish off / Cleanup High Level Wrapper
  - Also Implement a base class that allows for both TCP and UDP to be used in the network manager interchangeably
- Will Add the Compression packages to the public branch
  - Based of 3rd Part LZ4, Customized and optimized for my use-case 
- Example Project
- Better write up of performance testing

## Available On Request
- Login/Password implementation - Works great with the built in encryption for added security/authentication
- LZ4 Compression Implementation for packets

## Why Sleepy-Net?
- Easy to use, very little code required to get something going
- Fast & Optimized
  - Threaded Implementation - allowing for both Sync and Async Calls (Sync calls for things that require running on the unity main thread) 
    - This is not using the OS async callbacks (OS calls are slow), I use my own thread pool management
  - Pooled memory allocation - No GC Allocation when messages are sent/recieved past the first.
  - No Reflection/Attributes for message conversion or send/recv 
  - Very little overhead
  - Packet Spam/Large Packet TCP Protection
- Auto Port Forwarding - Using Open.Nat, Allows for *Most* Modern Routers to portforward for you when creating servers
- Full Encryption Support
  - RSA / AES communication can be enabled by default, and any message can be sent encrypted (even large files)
  - Built in Confirmation on connection (even in UDP) 
    - This will keep connections a bit more secure, knowing what client are what IP/Mac etc, not jsut any socket will connect
- Full Compression Support
  - Custom LZ4 Implemention (Coming in V0.3)

## Notes
- Uses Open.Nat (https://github.com/lontivero/Open.NAT) for port forwarding/management
- There may be other 'Sleepy' Packages this networking stack depends on. Will update other repo's shortly, but code visibility is here for this package/stack
  - If you wish to use this package with the other sleepy packages, feel free to contact me to get a full set of of the SleepyStack for unity

## Common Questions
- Why didn't you use C/C++ for more optimization? 
  - Few reasons, The underlying code of C# uses the same calls to hardware that C/C++ use, and taking the thread management on, along with other memory optimization/pooling etc. You really are not loosing any performance. I tried using a C++ socket implementation, and calling it from C# when needed, but turned out that the calls(Mainly the data parsing to C++ and from C++) added as much time as the benefit gained in C++, even when I used unmanaged C# memory parsing. Even more so when you take into account IL2CPP in unity, It all gets converted to C++ and compiled anyways. So I preferred to keep it all in the same language, easily editable/accessible when using in engine.
- Does this work on multiple platforms?
  - Yes - Tested on Windows, Linux, MacOS And Android. Cross platform support also. 
- Can I use this? 
  - Yes! by all means please use it, and learn from it. There are very few good sources for learning networking/sockets, even more so modern implementations.
  - I would greatly appreciate being credited if you do use or modify this package, and would love to know about projects that do use this package.
