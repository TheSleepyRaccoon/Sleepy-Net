# Sleepy-Net
A fast, simple to use, all in one networking system for c# (And Unity) 

## V0.1
- Low level wrapper for sockets, both UDP and TCP for raw data throughput that is easy to setup. 
- Medium level wrapper for sockers, both UDP and TCP
  - Message based communication, fast and optimised conversion of packets to useful data
  - Statically bound message types
  - Dynamic on the fly messages and responses that have callbacks
    - Can also be done with Unity Coroutines also, allowing to yield on the request
- Full Encryption Support
  - RSA / AES communication can be enabled by default, and any message can be sent encrypted (even large files)

## Coming Next
- Cleanup TCP version to match UDP Usage
- High level API for use within unity
  - Aiming to have a built-in way that requires very little code and will be easy to implement with the Unity Engine
- Example Project
- Better Write up of performance testing

## Why Sleepy-Net?
- Fast and optimised
- Easy to use, very little code required to get something going



## Notes
- Uses Open.Nat (https://github.com/lontivero/Open.NAT) for port forwarding/management
