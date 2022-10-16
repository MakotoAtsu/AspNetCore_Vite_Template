import { useState } from 'react'
import reactLogo from './assets/react.svg'
import './App.css'

function App() {
  const [count, setCount] = useState(0)
  const [backednResult , setResult] = useState('Please click button...')

  
  const fetchWeatherForecast = () => {
    fetch('/WeatherForecast')
      .then(resp => resp.json())
      .then(json => {
        setResult(JSON.stringify(json))
        console.log(json)
      })
  }

  const fetchMessage = () => {
    fetch('/WeatherForecast/Message')
      .then(resp => resp.text())
      .then(msg => setResult(msg))
  }

  return (
    <div className="App">
      <div>
        <a href="https://vitejs.dev" target="_blank">
          <img src="/vite.svg" className="logo" alt="Vite logo" />
        </a>
        <a href="https://reactjs.org" target="_blank">
          <img src={reactLogo} className="logo react" alt="React logo" />
        </a>
      </div>
      <h1>Vite + React</h1>
      <div className="card">
        <div>
          <button onClick={() => fetchMessage()}>
            Fetch Message from backend
          </button>
          <button onClick={() => setCount((count) => count + 1)}>
            count is {count}
          </button>
          <button onClick={() => fetchWeatherForecast()}>
            Fetch Weather Forecast from backend
          </button>
        </div>
        <code>
          {backednResult}
        </code>
        <p>
          Edit <code>src/App.tsx</code> and save to test HMR
        </p>
      </div>
      <p className="read-the-docs">
        Click on the Vite and React logos to learn more
      </p>
    </div>
  )
}

export default App
