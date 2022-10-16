<script setup lang="ts">
import { ref } from 'vue'

defineProps<{ msg: string }>()

const count = ref(0)
const backendResult = ref('Please click button...') 

const fetchWeatherForecast = () => {
  fetch('/WeatherForecast')
    .then(resp => resp.json())
    .then(json => {
      backendResult.value = json
      console.log(json)
    })
}

const fetchMessage = () => {
  fetch('/WeatherForecast/Message')
    .then(resp => resp.text())
    .then(msg => backendResult.value = msg)
}

</script>

<template>
  <h1>{{ msg }}</h1>

  <div class="card">
    <div>
      <button :style="{ margin: '10px' }" type="button" @click=fetchMessage>Fetch Message from backend</button>
      <button :style="{ margin: '10px' }" type="button" @click="count++">count is {{ count }}</button>
      <button :style="{ margin: '10px' }" type="button" @click=fetchWeatherForecast>Fetch Weather Forecast from backend</button>
    </div>
    <code>{{backendResult}}</code>
    <p>
      Edit
      <code>components/HelloWorld.vue</code> to test HMR
    </p>
  </div>

  <p>
    Check out
    <a href="https://vuejs.org/guide/quick-start.html#local" target="_blank"
      >create-vue</a
    >, the official Vue + Vite starter
  </p>
  <p>
    Install
    <a href="https://github.com/johnsoncodehk/volar" target="_blank">Volar</a>
    in your IDE for a better DX
  </p>
  <p class="read-the-docs">Click on the Vite and Vue logos to learn more</p>
</template>

<style scoped>
.read-the-docs {
  color: #888;
}
</style>
