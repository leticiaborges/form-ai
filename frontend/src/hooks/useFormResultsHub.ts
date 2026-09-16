
import { useEffect, useRef } from "react";
import * as signalR from '@microsoft/signalr';

export function useFormResultsHub(formId: string,
    onResultsChanged: () => void
) {
    const callbackRef = useRef(onResultsChanged);

    useEffect(() => {
        callbackRef.current = onResultsChanged;
    }, [onResultsChanged]);

    useEffect(() => {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/form-results', {
                accessTokenFactory: () => localStorage.getItem('accessToken') ?? ''
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();

        connection.on('ResultsUpdated', (changedFormId: string) => {
            if (changedFormId == formId)
                callbackRef.current();
        });

        const join = () =>
            connection.invoke('JoinFormResults', formId).catch((err) =>
                console.warn('Failed to join form results group', err)
            );

        connection.start().then(join).catch((err) =>
            console.warn('SignalR connection failed to start.', err));

        connection.onreconnected(join);

        return () => {
            connection.off('ResultsUpdated');
            connection.stop();
        };
    }, [formId]);
}