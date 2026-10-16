if __name__ == "__main__":
    import os
    import sys
    if os.environ.get("REMOTEDESK_DIAGNOSTICS"):
        import faulthandler
        diagnostic = open(os.environ["REMOTEDESK_DIAGNOSTICS"], "w", buffering=1)
        sys.stdout = sys.stderr = diagnostic
        faulthandler.enable(file=diagnostic)
    from remotedesk.app import main
    raise SystemExit(main())
