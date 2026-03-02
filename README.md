## About
Simple echo server implementation. 

### Prerequisites
- .NET SDK 8.0+ installed
- A terminal that supports multiple tabs

## How to run
1. Open terminal tab (TAB 1) for server. From the project root run:
````
dotnet build
````

2. Now in same tab (TAB 1), run:
````
dotnet run
````

You should now see output like:

````
Listening on X*
Listening on X*
Server running. Press Ctrl+C to stop.
````
*where X the server machines own IP addresses (one line per address)

3. Run clilents in separate tabs (TAB 2, TAB 3...)
__Using netcat__ `ncat`/ `nc` installed:

````
nc localhost 7007
````

4. Type text and press Enter (TAB 2, TAB 3...). The server echoes the same text back.
